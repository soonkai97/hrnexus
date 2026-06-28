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
import { AuthService, ToastComponent, ConfirmDialogComponent, ThemeToggleComponent, NotificationBellComponent, PermissionService, LangService, SignalRService } from 'shared-lib';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
  group: string;
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
  readonly permissions       = inject(PermissionService);

  // Translation files load asynchronously after bootstrap, so instant() inside a computed
  // returns the raw key on first render. Tick this on translation (re)loads so label
  // computeds (e.g. the avatar menu) re-evaluate once the file has arrived.
  private readonly _i18nReady = toSignal(
    merge(this.translate.onLangChange, this.translate.onDefaultLangChange),
    { initialValue: null });
  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  constructor() {
    this.permissions.load().subscribe();   // so in-app guards + *ncHasPermission resolve synchronously
    this.lang.syncFromApi();
    this.signalR.startConnection();

    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => {
      // Auto-close the drawer after navigating on mobile only; on desktop the sidebar
      // stays expanded so nav labels remain visible (icons-only hurt discoverability).
      if (typeof window !== 'undefined' && window.innerWidth < 769) {
        this.collapsed.set(true);
      }
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
    // Grouped + ordered by importance / daily-use frequency: operations → finance → comms → setup.
    { path: '/dashboard',     labelKey: 'nav.dashboard',     icon: 'pi pi-th-large',  group: 'operations' },
    { path: '/staff',         labelKey: 'nav.staff',         icon: 'pi pi-users',     group: 'operations' },
    { path: '/leave',         labelKey: 'nav.leave',         icon: 'pi pi-calendar',  group: 'operations' },
    { path: '/reports',       labelKey: 'nav.reports',       icon: 'pi pi-chart-bar', group: 'operations' },
    { path: '/payroll',        labelKey: 'nav.payroll',        icon: 'pi pi-wallet',    group: 'finance' },
    { path: '/locations',      labelKey: 'nav.locations',      icon: 'pi pi-map-marker', group: 'setup'  },
    { path: '/roles',         labelKey: 'nav.roles',         icon: 'pi pi-id-card',   group: 'setup'    },
    { path: '/payroll-locks',  labelKey: 'nav.payrollLocks',  icon: 'pi pi-lock',      group: 'setup' },
  ];

  /** Section order for the grouped sidebar. */
  private readonly navGroupOrder = [
    { key: 'operations', labelKey: 'nav.group.operations' },
    { key: 'finance',    labelKey: 'nav.group.finance'    },
    { key: 'comms',      labelKey: 'nav.group.comms'      },
    { key: 'setup',      labelKey: 'nav.group.setup'      },
  ];

  navItems = computed(() => {
    const roles   = this.auth.user()?.roles ?? [];
    const isAdmin = roles.includes('SUPERADMIN');

    return this.allNavItems.filter(item => !item.adminOnly || isAdmin);
  });

  /** Visible nav items grouped into labelled sections (empty groups dropped). */
  navGroups = computed(() => {
    const items = this.navItems();
    return this.navGroupOrder
      .map(g => ({ labelKey: g.labelKey, items: items.filter(i => i.group === g.key) }))
      .filter(g => g.items.length > 0);
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

  /** Language dropdown — native labels, a check on the active one. */
  readonly langMenuItems = computed<MenuItem[]>(() => {
    const cur = this.lang.current();
    return ([
      { label: 'English',     value: 'en' },
      { label: '中文',         value: 'zh' },
      { label: 'Tiếng Việt', value: 'vi' },
    ] as const).map(o => ({
      label: o.label,
      icon: cur === o.value ? 'pi pi-check' : 'pi pi-globe',
      command: () => this.lang.set(o.value),
    }));
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
