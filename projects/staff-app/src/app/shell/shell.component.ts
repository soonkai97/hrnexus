import { Component, signal, computed, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { CommonModule } from '@angular/common';
import { filter } from 'rxjs/operators';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, FeatureFlagService, FeatureKey, AnnouncementService } from 'shared-lib';
import { LangService } from '../core/lang.service';

interface NavItem { path: string; labelKey: string; icon: string; featureFlag?: FeatureKey; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet, RouterLink, RouterLinkActive,
    ToastComponent, ConfirmDialogComponent,
    NotificationBellComponent, ThemeToggleComponent,
    TranslatePipe,
  ],
  templateUrl: './shell.component.html',
  styleUrl:    './shell.component.scss',
})
export class ShellComponent {
  private readonly auth            = inject(AuthService);
  private readonly router          = inject(Router);
  readonly lang                    = inject(LangService);
  readonly featureSvc              = inject(FeatureFlagService);
  private readonly announcementSvc = inject(AnnouncementService);

  // Start collapsed on mobile
  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  /** Unread announcement count — refreshed on every navigation so it stays current */
  unreadAnnouncements = signal(0);

  constructor() {
    // Load feature flags, then load initial unread count
    this.featureSvc.load().subscribe(() => {
      this.refreshUnread();
    });
    this.lang.syncFromApi();

    // Refresh badge and collapse sidebar on every navigation
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      if (typeof window !== 'undefined' && window.innerWidth < 769) {
        this.collapsed.set(true);
      }
      // Refresh unread count so badge reflects any reads done on the announcements page
      this.refreshUnread();
    });
  }

  private refreshUnread(): void {
    if (!this.featureSvc.isEnabled('announcements')) return;
    this.announcementSvc.getMine().subscribe(list =>
      this.unreadAnnouncements.set(list.filter(a => !a.isRead).length)
    );
  }

  user = computed(() => this.auth.user());

  initials = computed(() => {
    const n = this.auth.user()?.fullName ?? '';
    return n.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2);
  });

  /** Human-readable role label shown in sidebar footer */
  userRoleLabel = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    if (roles.includes('manager')) return 'Manager';
    if (roles.includes('admin'))   return 'Admin';
    if (roles.length > 0)          return roles[0].charAt(0).toUpperCase() + roles[0].slice(1);
    return 'Staff';
  });

  private readonly allNavItems: NavItem[] = [
    { path: '/log-shift',       labelKey: 'nav.logShift',       icon: '⏱'  },
    { path: '/pay-summary',     labelKey: 'nav.pay',            icon: '💵', featureFlag: 'paySummary'     },
    { path: '/tips-history',    labelKey: 'nav.tipsHistory',    icon: '💵', featureFlag: 'tipsHistory'    },
    { path: '/my-leave',        labelKey: 'nav.leave',          icon: '🌴'  },
    { path: '/announcements',   labelKey: 'nav.announcements',  icon: '📢', featureFlag: 'announcements' },
    { path: '/messaging',       labelKey: 'nav.messaging',      icon: '💬', featureFlag: 'messagingStaff' },
    { path: '/profile',         labelKey: 'nav.profile',        icon: '👤'  },
  ];

  /** Reactive nav — re-computed whenever feature flags change */
  navItems = computed(() => {
    const flags = this.featureSvc.flags();
    return this.allNavItems.filter(item => !item.featureFlag || flags[item.featureFlag]);
  });

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void { this.auth.logout(); }
}
