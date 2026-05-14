import { Component, signal, computed, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { CommonModule } from '@angular/common';
import { filter } from 'rxjs/operators';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, ToastComponent, ConfirmDialogComponent, ThemeToggleComponent, NotificationBellComponent, FeatureFlagService, FeatureKey, LangService } from 'shared-lib';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
  featureFlag?: FeatureKey; // hidden until the flag is enabled
  adminOnly?: boolean;       // hidden for managers; only admins see it
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, ToastComponent, ConfirmDialogComponent, NotificationBellComponent, ThemeToggleComponent, TranslatePipe],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly auth    = inject(AuthService);
  private readonly router  = inject(Router);
  readonly lang            = inject(LangService);
  readonly featureSvc      = inject(FeatureFlagService);

  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  constructor() {
    // Load feature flags and sync language preference from backend
    this.featureSvc.load().subscribe();
    this.lang.syncFromApi();

    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
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
    if (roles.includes('admin'))   return 'Admin';
    if (roles.includes('manager')) return 'Manager';
    return roles[0] ? roles[0].charAt(0).toUpperCase() + roles[0].slice(1) : 'Staff';
  });

  private readonly allNavItems: NavItem[] = [
    // ── Daily operations ─────────────────────────────
    { path: '/dashboard',     labelKey: 'nav.dashboard',     icon: '◈'  },
    { path: '/leave',         labelKey: 'nav.leave',         icon: '📅'  },
    { path: '/announcements', labelKey: 'nav.announcements', icon: '📢', featureFlag: 'announcements'  },
    { path: '/messaging',     labelKey: 'nav.messaging',     icon: '💬', featureFlag: 'messagingAdmin' },
    // ── People & payroll ─────────────────────────────
    { path: '/staff',         labelKey: 'nav.staff',         icon: '👥'  },
    { path: '/reports',       labelKey: 'nav.reports',       icon: '📊'  },
    { path: '/tips',          labelKey: 'nav.tips',          icon: '💰', featureFlag: 'tips'           },
    { path: '/payroll',       labelKey: 'nav.payroll',       icon: '💰', featureFlag: 'payroll'        },
    // ── Setup & config ───────────────────────────────
    { path: '/locations',     labelKey: 'nav.locations',     icon: '📍'  },
    { path: '/overtime',      labelKey: 'nav.otRules',       icon: '⏱',  featureFlag: 'otRules'        },
    { path: '/roles',         labelKey: 'nav.roles',         icon: '🏷'  },
    // ── Admin only ────────────────────────────────────
    // /features intentionally omitted from nav — PIN-protected, direct URL only
  ];

  navItems = computed(() => {
    const flags   = this.featureSvc.flags();
    const roles   = this.auth.user()?.roles ?? [];
    const isAdmin = roles.includes('admin');

    return this.allNavItems.filter(item =>
      (!item.featureFlag || flags[item.featureFlag]) &&
      (!item.adminOnly   || isAdmin)
    );
  });

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void { this.auth.logout(); }
}
