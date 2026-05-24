import { Component, signal, computed, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { TranslatePipe } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, ToastComponent, ConfirmDialogComponent, ThemeToggleComponent, NotificationBellComponent, FeatureFlagService, FeatureKey, LangService, SignalRService, NotificationService } from 'shared-lib';

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
export class ShellComponent {
  private readonly auth      = inject(AuthService);
  private readonly router    = inject(Router);
  private readonly signalR   = inject(SignalRService);
  readonly lang              = inject(LangService);
  readonly featureSvc        = inject(FeatureFlagService);

  collapsed = signal(typeof window !== 'undefined' && window.innerWidth < 769);

  readonly notifSvc          = inject(NotificationService);
  readonly unreadNotifications = computed(() => this.notifSvc.unreadCount());

  constructor() {
    this.featureSvc.load().subscribe();
    this.lang.syncFromApi();
    this.signalR.startConnection();

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
    if (roles.includes('ADMIN'))   return 'Admin';
    if (roles.includes('MANAGER')) return 'Manager';
    return roles[0] ?? 'Staff';
  });

  private readonly allNavItems: NavItem[] = [
    { path: '/dashboard',     labelKey: 'nav.dashboard',     icon: 'pi pi-th-large'                                   },
    { path: '/leave',         labelKey: 'nav.leave',         icon: 'pi pi-calendar'                                   },
    { path: '/announcements', labelKey: 'nav.announcements', icon: 'pi pi-megaphone', featureFlag: 'announcements'    },
    { path: '/staff',         labelKey: 'nav.staff',         icon: 'pi pi-users'                                      },
    { path: '/reports',       labelKey: 'nav.reports',       icon: 'pi pi-chart-bar'                                  },
    { path: '/tips',           labelKey: 'nav.tips',           icon: 'pi pi-dollar',    featureFlag: 'tips'             },
    { path: '/payroll',        labelKey: 'nav.payroll',        icon: 'pi pi-wallet',    featureFlag: 'payroll'          },
    { path: '/staff-ledger',   labelKey: 'nav.staffLedger',   icon: 'pi pi-book',         featureFlag: 'staffLedger' },
    { path: '/staff-settings', labelKey: 'nav.staffSettings', icon: 'pi pi-sliders-h',  featureFlag: 'staffLedger' },
    { path: '/locations',      labelKey: 'nav.locations',      icon: 'pi pi-map-marker'                                },
    { path: '/roles',         labelKey: 'nav.roles',         icon: 'pi pi-id-card'                                    },
  ];

  navItems = computed(() => {
    const flags   = this.featureSvc.flags();
    const roles   = this.auth.user()?.roles ?? [];
    const isAdmin = roles.includes('ADMIN');

    return this.allNavItems.filter(item =>
      (!item.featureFlag || flags[item.featureFlag]) &&
      (!item.adminOnly   || isAdmin)
    );
  });

  readonly avatarMenuItems = computed<MenuItem[]>(() => [
    { label: this.user()?.fullName ?? '', disabled: true },
    { separator: true },
    { label: 'Profile', icon: 'pi pi-user', routerLink: '/account' },
    { separator: true },
    { label: 'Sign Out', icon: 'pi pi-power-off', command: () => this.logout() },
  ]);

  toggleSidebar(): void { this.collapsed.update(v => !v); }
  logout(): void { this.signalR.stopConnection(); this.auth.logout(); }
}
