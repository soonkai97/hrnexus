import { AfterViewInit, Component, DestroyRef, ElementRef, signal, computed, inject, ViewChild } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { merge } from 'rxjs';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, ToastComponent, ConfirmDialogComponent, ThemeToggleComponent, NotificationBellComponent, FeatureFlagService, FeatureKey, LangService, SignalRService } from 'shared-lib';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
  featureFlag?: FeatureKey;
  adminOnly?: boolean;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, TranslatePipe, Button, BadgeModule, TooltipModule, Menu],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent implements AfterViewInit {
  @ViewChild('mainContent') private mainContent!: ElementRef<HTMLElement>;
  private readonly destroyRef = inject(DestroyRef);

  // ── Offline detection ──────────────────────────────────────────────────────
  isOffline = signal(false);

  // Shown the instant Sign Out is tapped so the navigation/teardown gap doesn't
  // look like a frozen screen.
  signingOut = signal(false);

  // ── Pull-to-refresh ────────────────────────────────────────────────────────
  pullProgress = signal(0);   // 0–100
  private ptStartY = 0;
  private ptPulling = false;
  private ptReloading = false;
  private readonly PULL_THRESHOLD = 70; // px

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

  private readonly auth      = inject(AuthService);
  private readonly router    = inject(Router);
  private readonly signalR   = inject(SignalRService);
  readonly lang              = inject(LangService);
  private readonly translate = inject(TranslateService);
  readonly featureSvc        = inject(FeatureFlagService);

  // Translation files load asynchronously after bootstrap, so instant() inside a computed
  // returns the raw key on first render. Tick this on translation (re)loads so label
  // computeds (e.g. the avatar menu) re-evaluate once the file has arrived.
  private readonly _i18nReady = toSignal(
    merge(this.translate.onLangChange, this.translate.onDefaultLangChange),
    { initialValue: null });
  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  constructor() {
    this.featureSvc.load().subscribe();
    this.lang.syncFromApi();
    this.signalR.startConnection();

    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => {
      this.collapsed.set(true);
    });
  }

  user = computed(() => this.auth.user());

  initials = computed(() => {
    const n = this.auth.user()?.fullName ?? '';
    return n.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2);
  });

  userRoleLabel = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    if (roles.includes('SUPERADMIN'))   return 'Admin';
    if (roles.includes('MANAGER')) return 'Manager';
    return roles[0] ?? 'Staff';
  });

  private readonly allNavItems: NavItem[] = [
    // Ordered by importance / daily-use frequency: operations → finance → comms → setup.
    // Daily operations
    { path: '/dashboard',     labelKey: 'nav.dashboard',     icon: 'pi pi-th-large'                                   },
    { path: '/staff',         labelKey: 'nav.staff',         icon: 'pi pi-users'                                      },
    { path: '/leave',         labelKey: 'nav.leave',         icon: 'pi pi-calendar'                                   },
    { path: '/reports',       labelKey: 'nav.reports',       icon: 'pi pi-chart-bar'                                  },
    // Finance
    { path: '/staff-ledger',   labelKey: 'nav.staffLedger',   icon: 'pi pi-book',      featureFlag: 'staffLedger'     },
    { path: '/payroll',        labelKey: 'nav.payroll',        icon: 'pi pi-wallet',    featureFlag: 'payroll'          },
    { path: '/tips',           labelKey: 'nav.tips',           icon: 'pi pi-dollar',    featureFlag: 'tips'             },
    // Communication
    { path: '/announcements', labelKey: 'nav.announcements', icon: 'pi pi-megaphone', featureFlag: 'announcements'    },
    // Setup / governance (configured rarely)
    { path: '/locations',      labelKey: 'nav.locations',      icon: 'pi pi-map-marker'                                },
    { path: '/roles',         labelKey: 'nav.roles',         icon: 'pi pi-id-card'                                    },
    { path: '/staff-settings', labelKey: 'nav.staffSettings', icon: 'pi pi-sliders-h', featureFlag: 'staffLedger'     },
    { path: '/payroll-locks',  labelKey: 'nav.payrollLocks',  icon: 'pi pi-lock'                                       },
  ];

  navItems = computed(() => {
    const flags   = this.featureSvc.flags();
    const roles   = this.auth.user()?.roles ?? [];
    const isAdmin = roles.includes('SUPERADMIN');

    return this.allNavItems.filter(item =>
      (!item.featureFlag || flags[item.featureFlag]) &&
      (!item.adminOnly   || isAdmin)
    );
  });

  readonly avatarMenuItems = computed<MenuItem[]>(() => {
    this.lang.current(); // re-evaluate labels on language change
    this._i18nReady();   // re-evaluate once translation files finish loading
    return [
      { label: this.user()?.fullName ?? '', disabled: true },
      { separator: true },
      { label: this.translate.instant('account.title'), icon: 'pi pi-user', routerLink: '/account' },
      { separator: true },
      { label: this.translate.instant('shell.signOut'), icon: 'pi pi-power-off', command: () => this.logout() },
    ];
  });

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

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void {
    this.signingOut.set(true);
    // Defer one macrotask so the overlay paints before the route teardown/navigation.
    setTimeout(() => { this.signalR.stopConnection(); this.auth.logout(); });
  }
}
