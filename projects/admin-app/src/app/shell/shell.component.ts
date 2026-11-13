import { AfterViewInit, Component, DestroyRef, ElementRef, signal, computed, inject, ViewChild } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, ToastComponent, ConfirmDialogComponent, ThemeToggleComponent, NotificationBellComponent, PermissionService, SignalRService } from 'shared-lib';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  group: string;
  adminOnly?: boolean;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, Button, BadgeModule, TooltipModule, Menu],
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
  readonly permissions       = inject(PermissionService);

  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  constructor() {
    this.permissions.load().subscribe();   // so in-app guards + *ncHasPermission resolve synchronously
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
    { path: '/dashboard',     label: 'Dashboard',     icon: 'pi pi-th-large',  group: 'operations' },
    { path: '/staff',         label: 'Staff',         icon: 'pi pi-users',     group: 'operations' },
    { path: '/leave',         label: 'Leave Requests',         icon: 'pi pi-calendar',  group: 'operations' },
    { path: '/cash-advances',  label: 'Cash Advances',   icon: 'pi pi-money-bill', group: 'finance' },
    { path: '/roles',         label: 'Jobs',         icon: 'pi pi-id-card',   group: 'setup'    },
    { path: '/payroll-locks',  label: 'Payroll Locks',  icon: 'pi pi-lock',      group: 'setup' },
  ];

  /** Section order for the grouped sidebar. */
  private readonly navGroupOrder = [
    { key: 'operations', label: 'Operations' },
    { key: 'finance',    label: 'Finance'    },
    { key: 'setup',      label: 'Setup'      },
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
      .map(g => ({ label: g.label, items: items.filter(i => i.group === g.key) }))
      .filter(g => g.items.length > 0);
  });

  readonly avatarMenuItems = computed<MenuItem[]>(() => {
    return [
      { label: this.user()?.fullName ?? '', disabled: true },
      { separator: true },
      { label: 'My Account', icon: 'pi pi-user', routerLink: '/account' },
      { separator: true },
      { label: 'Sign out', icon: 'pi pi-power-off', command: () => this.logout() },
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
