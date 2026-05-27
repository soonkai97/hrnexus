import { Component, signal, computed, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { interval } from 'rxjs';
import { filter, startWith } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, FeatureFlagService, FeatureKey, AnnouncementService, LangService, SignalRService, NotificationService, LeaveService, ToastService } from 'shared-lib';

const LEAVE_SNAPSHOT_KEY = 'nc_leave_status_snapshot';
const POLL_INTERVAL_MS   = 60_000;

interface NavItem { path: string; labelKey: string; icon: string; featureFlag?: FeatureKey; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive,
    ToastComponent, ConfirmDialogComponent,
    NotificationBellComponent, ThemeToggleComponent,
    TranslatePipe, Button, BadgeModule, TooltipModule, Menu,
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
  private readonly leaveSvc        = inject(LeaveService);
  private readonly toast           = inject(ToastService);
  private readonly translate       = inject(TranslateService);
  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  unreadAnnouncements = signal(0);
  unreadNotifications = computed(() => this.notifSvc.unreadCount());
  leaveChangeCount    = signal(0);

  constructor() {
    this.featureSvc.load().subscribe(() => { this.refreshUnread(); });
    this.lang.syncFromApi();
    this.signalR.startConnection();

    // Background poll: refresh announcement badge + detect leave status changes
    interval(POLL_INTERVAL_MS).pipe(
      startWith(0),
      takeUntilDestroyed(),
      filter(() => !!this.auth.user()),
    ).subscribe(() => {
      this.refreshUnread();
      this.checkLeaveChanges();
    });

    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(e => {
      this.collapsed.set(true);
      this.refreshUnread();
      // Clear leave badge when the user opens My Leave
      if ((e as NavigationEnd).urlAfterRedirects.startsWith('/my-leave')) {
        this.leaveChangeCount.set(0);
        this.syncLeaveSnapshot();
      }
    });
  }

  private refreshUnread(): void {
    if (!this.featureSvc.isEnabled('announcements')) return;
    this.announcementSvc.getMine().subscribe(list =>
      this.unreadAnnouncements.set(list.filter(a => !a.isRead).length)
    );
  }

  /** Compare live leave records against the stored snapshot; toast on status changes. */
  private checkLeaveChanges(): void {
    this.leaveSvc.getMine().subscribe({
      next: records => {
        let snapshot: Record<string, string> = {};
        try {
          const raw = localStorage.getItem(LEAVE_SNAPSHOT_KEY);
          snapshot = raw ? JSON.parse(raw) : {};
        } catch { snapshot = {}; }

        let newChanges = 0;
        records.forEach(r => {
          const prev = snapshot[r.id];
          if (prev === 'pending') {
            if (r.status === 'approved') {
              this.toast.success(this.translate.instant('polling.leaveApproved'));
              newChanges++;
            } else if (r.status === 'rejected') {
              this.toast.error(this.translate.instant('polling.leaveRejected'));
              newChanges++;
            }
          }
          snapshot[r.id] = r.status;
        });

        try { localStorage.setItem(LEAVE_SNAPSHOT_KEY, JSON.stringify(snapshot)); } catch { /* quota */ }
        if (newChanges > 0) this.leaveChangeCount.update(n => n + newChanges);
      },
      error: () => { /* silent — background poll */ },
    });
  }

  /** Silently update snapshot without showing toasts (called after user visits My Leave). */
  private syncLeaveSnapshot(): void {
    this.leaveSvc.getMine().subscribe({
      next: records => {
        const snapshot: Record<string, string> = {};
        records.forEach(r => snapshot[r.id] = r.status);
        try { localStorage.setItem(LEAVE_SNAPSHOT_KEY, JSON.stringify(snapshot)); } catch { /* quota */ }
      },
      error: () => {},
    });
  }

  user = computed(() => this.auth.user());

  initials = computed(() => {
    const n = this.auth.user()?.fullName ?? '';
    return n.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2);
  });

  userRoleLabel = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    if (roles.includes('MANAGER')) return 'Manager';
    if (roles.includes('SUPERADMIN'))   return 'Admin';
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

  readonly avatarMenuItems = computed<MenuItem[]>(() => [
    { label: this.user()?.fullName ?? '', disabled: true },
    { separator: true },
    { label: 'Profile', icon: 'pi pi-user', routerLink: '/profile' },
    { separator: true },
    { label: 'Sign Out', icon: 'pi pi-power-off', command: () => this.logout() },
  ]);

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void { this.signalR.stopConnection(); this.auth.logout(); }
}
