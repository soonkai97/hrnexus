import { AfterViewInit, Component, DestroyRef, ElementRef, signal, computed, inject, ViewChild } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { interval } from 'rxjs';
import { filter, startWith } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, ToastComponent, ConfirmDialogComponent, ThemeToggleComponent, NotificationBellComponent, PermissionService, PermissionKey, SignalRService, LeaveService, ToastService } from 'shared-lib';

const LEAVE_SNAPSHOT_KEY = 'nc_leave_status_snapshot';
const POLL_INTERVAL_MS   = 60_000;

interface NavItem {
  path: string;
  label: string;
  icon: string;
  group: string;
  /** Shown only to people holding this permission; absent = every signed-in user. */
  permission?: PermissionKey;
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
  private readonly leaveSvc  = inject(LeaveService);
  private readonly toast     = inject(ToastService);
  readonly permissions       = inject(PermissionService);

  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  /** Leave decisions the person has not seen yet; shown as a badge on My Leave. */
  leaveChangeCount = signal(0);

  constructor() {
    this.permissions.load().subscribe();   // so in-app guards + the permission-filtered menu resolve
    this.signalR.startConnection();

    // Background poll: tell the person when a pending leave request was decided.
    interval(POLL_INTERVAL_MS).pipe(
      startWith(0),
      takeUntilDestroyed(),
      filter(() => !!this.auth.user()),
    ).subscribe(() => this.checkLeaveChanges());

    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(e => {
      // Auto-close the drawer after navigating on mobile only; on desktop the sidebar
      // stays expanded so nav labels remain visible (icons-only hurt discoverability).
      if (typeof window !== 'undefined' && window.innerWidth < 769) {
        this.collapsed.set(true);
      }
      // Opening My Leave clears the badge and records what the person has now seen.
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
          if (snapshot[r.id] === 'pending') {
            if (r.status === 'approved') {
              this.toast.success('Your leave request has been approved.');
              newChanges++;
            } else if (r.status === 'rejected') {
              this.toast.error('Your leave request has been rejected.');
              newChanges++;
            }
          }
          snapshot[r.id] = r.status;
        });

        try { localStorage.setItem(LEAVE_SNAPSHOT_KEY, JSON.stringify(snapshot)); } catch { /* quota */ }
        if (newChanges > 0) this.leaveChangeCount.update(n => n + newChanges);
        this.isCheckingLeave = false;
      },
      error: () => { this.isCheckingLeave = false; /* silent: background poll */ },
    });
  }

  private isSyncingLeave = false;

  /** Silently update the snapshot without toasts (called after the person visits My Leave). */
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

  private readonly allNavItems: NavItem[] = [
    // "My work" is for everyone. The rest appear only to people holding the permission the page
    // needs (the server enforces the same keys; hiding the link is a convenience, not security).
    { path: '/home',          label: 'Home',            icon: 'pi pi-home',       group: 'work' },
    { path: '/my-leave',      label: 'My Leave',        icon: 'pi pi-calendar',   group: 'work' },
    { path: '/profile',       label: 'Profile',         icon: 'pi pi-user',       group: 'work' },
    { path: '/dashboard',     label: 'Dashboard',       icon: 'pi pi-th-large',   group: 'operations', permission: 'reports.view' },
    { path: '/staff',         label: 'Staff',           icon: 'pi pi-users',      group: 'operations', permission: 'staff.view' },
    { path: '/leave',         label: 'Leave Requests',  icon: 'pi pi-inbox',      group: 'operations', permission: 'leave.approve' },
    { path: '/cash-advances', label: 'Cash Advances',   icon: 'pi pi-money-bill', group: 'finance',    permission: 'cashadvance.manage' },
    { path: '/payroll-locks', label: 'Payroll Locks',   icon: 'pi pi-lock',       group: 'finance',    permission: 'payroll.lock' },
    { path: '/roles',         label: 'Jobs',            icon: 'pi pi-id-card',    group: 'setup',      permission: 'roles.manage' },
  ];

  /** Section order for the grouped sidebar. */
  private readonly navGroupOrder = [
    { key: 'work',       label: 'My work'    },
    { key: 'operations', label: 'Operations' },
    { key: 'finance',    label: 'Finance'    },
    { key: 'setup',      label: 'Setup'      },
  ];

  navItems = computed(() =>
    this.allNavItems.filter(item => !item.permission || this.permissions.has(item.permission)));

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
      { label: 'Profile', icon: 'pi pi-user', routerLink: '/profile' },
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
