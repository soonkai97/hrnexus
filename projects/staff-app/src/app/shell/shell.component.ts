import { AfterViewInit, Component, DestroyRef, ElementRef, signal, computed, inject, ViewChild } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { interval, merge } from 'rxjs';
import { filter, startWith } from 'rxjs/operators';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, FeatureFlagService, FeatureKey, LangService, SignalRService, NotificationService, LeaveService, ToastService } from 'shared-lib';

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
export class ShellComponent implements AfterViewInit {
  @ViewChild('mainContent') private mainContent!: ElementRef<HTMLElement>;
  private readonly destroyRef = inject(DestroyRef);

  isOffline = signal(false);

  pullProgress = signal(0);
  private ptStartY = 0;
  private ptPulling = false;
  private ptReloading = false;
  private readonly PULL_THRESHOLD = 70;

  private readonly onOnline  = () => this.isOffline.set(false);
  private readonly onOffline = () => this.isOffline.set(true);

  private readonly onTouchStart = (e: TouchEvent) => {
    if (this.mainContent?.nativeElement.scrollTop === 0) {
      this.ptStartY = e.touches[0]?.clientY ?? 0;
      this.ptPulling = true;
    }
  };
  private readonly onTouchMove = (e: TouchEvent) => {
    if (!this.ptPulling) return;
    const delta = (e.touches[0]?.clientY ?? 0) - this.ptStartY;
    if (delta > 0 && this.mainContent?.nativeElement.scrollTop === 0) {
      e.preventDefault();
      this.pullProgress.set(Math.min(100, Math.round((delta / this.PULL_THRESHOLD) * 100)));
    } else {
      this.ptPulling = false;
      this.pullProgress.set(0);
    }
  };
  private readonly onTouchEnd = () => {
    if (this.ptReloading) return;
    if (this.pullProgress() >= 100) { this.ptReloading = true; window.location.reload(); return; }
    this.pullProgress.set(0);
    this.ptPulling = false;
  };

  ngAfterViewInit(): void {
    if (typeof window === 'undefined') return;
    this.isOffline.set(!navigator.onLine);
    const el = this.mainContent.nativeElement;
    window.addEventListener('online',     this.onOnline);
    window.addEventListener('offline',    this.onOffline);
    el.addEventListener('touchstart', this.onTouchStart, { passive: true });
    el.addEventListener('touchmove',  this.onTouchMove,  { passive: false });
    el.addEventListener('touchend',   this.onTouchEnd,   { passive: true });
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('online',  this.onOnline);
      window.removeEventListener('offline', this.onOffline);
      el.removeEventListener('touchstart', this.onTouchStart);
      el.removeEventListener('touchmove',  this.onTouchMove);
      el.removeEventListener('touchend',   this.onTouchEnd);
    });
  }

  private readonly auth            = inject(AuthService);
  private readonly router          = inject(Router);
  private readonly signalR         = inject(SignalRService);
  readonly lang                    = inject(LangService);
  readonly featureSvc              = inject(FeatureFlagService);
  readonly notifSvc                = inject(NotificationService);
  private readonly leaveSvc        = inject(LeaveService);
  private readonly toast           = inject(ToastService);
  private readonly translate       = inject(TranslateService);

  // Translation files load asynchronously after bootstrap, so instant() inside a computed
  // returns the raw key on first render. Tick this on translation (re)loads so label
  // computeds (e.g. the avatar menu) re-evaluate once the file has arrived.
  private readonly _i18nReady = toSignal(
    merge(this.translate.onLangChange, this.translate.onDefaultLangChange),
    { initialValue: null });
  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  unreadNotifications = computed(() => this.notifSvc.unreadCount());
  leaveChangeCount    = signal(0);

  constructor() {
    this.featureSvc.load().subscribe();
    this.lang.syncFromApi();
    this.signalR.startConnection();

    // Background poll: detect leave status changes
    interval(POLL_INTERVAL_MS).pipe(
      startWith(0),
      takeUntilDestroyed(),
      filter(() => !!this.auth.user()),
    ).subscribe(() => {
      this.checkLeaveChanges();
    });

    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(e => {
      this.collapsed.set(true);
      // Clear leave badge when the user opens My Leave
      if ((e as NavigationEnd).urlAfterRedirects.startsWith('/my-leave')) {
        this.leaveChangeCount.set(0);
        this.syncLeaveSnapshot();
      }
    });
  }

  private isCheckingLeave = false;

  /** Compare live leave records against the stored snapshot; toast on status changes. */
  private checkLeaveChanges(): void {
    if (this.isCheckingLeave) return; // dedup: previous poll still in-flight
    this.isCheckingLeave = true;
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
        this.isCheckingLeave = false;
      },
      error: () => { this.isCheckingLeave = false; /* silent — background poll */ },
    });
  }

  private isSyncingLeave = false;

  /** Silently update snapshot without showing toasts (called after user visits My Leave). */
  private syncLeaveSnapshot(): void {
    if (this.isSyncingLeave) return; // dedup: previous sync still in-flight
    this.isSyncingLeave = true;
    this.leaveSvc.getMine().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: records => {
        const snapshot: Record<string, string> = {};
        records.forEach(r => snapshot[r.id] = r.status);
        try { localStorage.setItem(LEAVE_SNAPSHOT_KEY, JSON.stringify(snapshot)); } catch { /* quota */ }
        this.isSyncingLeave = false;
      },
      error: () => { this.isSyncingLeave = false; },
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
    { path: '/my-leave',      labelKey: 'nav.leave',         icon: 'pi pi-calendar'                                  },
  ];

  navItems = computed(() => {
    const flags = this.featureSvc.flags();
    return this.allNavItems.filter(item => !item.featureFlag || flags[item.featureFlag]);
  });

  readonly avatarMenuItems = computed<MenuItem[]>(() => {
    this.lang.current(); // re-evaluate labels on language change
    this._i18nReady();   // re-evaluate once translation files finish loading
    return [
      { label: this.user()?.fullName ?? '', disabled: true },
      { separator: true },
      { label: this.translate.instant('nav.profile'), icon: 'pi pi-user', routerLink: '/profile' },
      { separator: true },
      { label: this.translate.instant('profile.signOut'), icon: 'pi pi-power-off', command: () => this.logout() },
    ];
  });

  // Shown the instant Sign Out is tapped so the navigation/teardown gap doesn't
  // look like a frozen screen.
  signingOut = signal(false);

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void {
    this.signingOut.set(true);
    // Defer one macrotask so the overlay paints before the route teardown/navigation.
    setTimeout(() => { this.signalR.stopConnection(); this.auth.logout(); });
  }
}
