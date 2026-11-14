import { Routes } from '@angular/router';
import { authGuard, permissionGuard, unsavedChangesGuard } from 'shared-lib';
import { ShellComponent } from './shell/shell.component';

/**
 * One app for everyone. Self-service pages (home, my leave, profile) need only a signed-in account;
 * every management page is gated by the permission the server also enforces on its endpoints.
 */
export const routes: Routes = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.AUTH_ROUTES),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      // Self-service: any signed-in, active account.
      {
        path: 'home',
        loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent),
      },
      {
        path: 'my-leave',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/my-leave/my-leave.component').then((m) => m.MyLeaveComponent),
      },
      {
        path: 'profile',
        loadChildren: () =>
          import('./features/profile/profile.routes').then((m) => m.PROFILE_ROUTES),
      },

      // Management: permission-gated.
      {
        path: 'dashboard',
        canActivate: [permissionGuard('reports.view', '/home')],
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'staff',
        canActivate: [permissionGuard('staff.view', '/home')],
        loadChildren: () => import('./features/staff/staff.routes').then((m) => m.STAFF_ROUTES),
      },
      {
        path: 'roles',
        canActivate: [permissionGuard('roles.manage', '/home')],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/roles/roles.component').then((m) => m.RolesComponent),
      },
      {
        path: 'policy',
        canActivate: [permissionGuard('policy.manage', '/home')],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/policy/policy.component').then((m) => m.PolicyComponent),
      },
      {
        path: 'leave',
        canActivate: [permissionGuard('leave.approve', '/home')],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/leave/leave.component').then((m) => m.LeaveComponent),
      },
      {
        path: 'payroll-locks',
        canActivate: [permissionGuard('payroll.lock', '/home')],
        loadComponent: () =>
          import('./features/payroll-locks/payroll-locks.component').then(
            (m) => m.PayrollLocksComponent,
          ),
      },
      {
        path: 'payroll-rates',
        canActivate: [permissionGuard('payroll.view', '/home')],
        loadComponent: () =>
          import('./features/payroll-rates/payroll-rates.component').then(
            (m) => m.PayrollRatesComponent,
          ),
      },
      {
        path: 'cash-advances',
        canActivate: [permissionGuard('cashadvance.manage', '/home')],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/cash-advances/cash-advances.component').then(
            (m) => m.CashAdvancesComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'home' },
];
