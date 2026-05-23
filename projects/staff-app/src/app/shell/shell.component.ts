import { Component, signal, computed, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { TranslatePipe } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { AuthService, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, FeatureFlagService, FeatureKey, AnnouncementService, LangService, SignalRService, NotificationService } from 'shared-lib';

interface NavItem { path: string; labelKey: string; icon: string; featureFlag?: FeatureKey; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive,
    ToastComponent, ConfirmDialogComponent,
    NotificationBellComponent, ThemeToggleComponent,
    TranslatePipe, Button, BadgeModule, TooltipModule,
  ],
  templateUrl: './shell.component.html',
  styleUrl:    './shell.component.scss',
})
export class ShellComponent {
  private readonly auth            = inject(AuthService);
  private readonly router          = inject(Router);
  private readonly signalR         = inject(SignalRService);
  readonly lang                    = inject(LangService);
  readonly featureSvc              = inject(FeatureFlagService);
  private readonly announcementSvc = inject(AnnouncementService);
  readonly notifSvc                = inject(NotificationService);

  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  unreadAnnouncements = signal(0);
  unreadNotifications = computed(() => this.notifSvc.unreadCount());

  constructor() {
    this.featureSvc.load().subscribe(() => { this.refreshUnread(); });
    this.lang.syncFromApi();
    this.signalR.startConnection();

    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      if (typeof window !== 'undefined' && window.innerWidth < 769) {
        this.collapsed.set(true);
      }
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

  userRoleLabel = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    if (roles.includes('MANAGER')) return 'Manager';
    if (roles.includes('ADMIN'))   return 'Admin';
    return roles[0] ?? 'Staff';
  });

  private readonly allNavItems: NavItem[] = [
    { path: '/home',          labelKey: 'nav.home',          icon: 'pi pi-home'                                      },
    { path: '/log-shift',     labelKey: 'nav.logShift',      icon: 'pi pi-clock'                                     },
    { path: '/pay-summary',   labelKey: 'nav.pay',           icon: 'pi pi-wallet',    featureFlag: 'paySummary'      },
    { path: '/tips-history',  labelKey: 'nav.tipsHistory',   icon: 'pi pi-dollar',    featureFlag: 'tipsHistory'     },
    { path: '/my-leave',      labelKey: 'nav.leave',         icon: 'pi pi-calendar'                                  },
    { path: '/announcements', labelKey: 'nav.announcements', icon: 'pi pi-megaphone', featureFlag: 'announcements'   },
  ];

  navItems = computed(() => {
    const flags = this.featureSvc.flags();
    return this.allNavItems.filter(item => !item.featureFlag || flags[item.featureFlag]);
  });

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void { this.signalR.stopConnection(); this.auth.logout(); }
}
