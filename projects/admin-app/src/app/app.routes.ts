import { Routes } from '@angular/router';
import { authGuard, permissionGuard, unsavedChangesGuard } from 'shared-lib';
import { ShellComponent } from './shell/shell.component';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then(m => m.AUTH_ROUTES),
  },
  {
    path: '',
    component: ShellComponent,
    // Admin-portal access is gated by the portal.admin permission (was: manager/admin role).
    canActivate: [authGuard, permissionGuard('portal.admin', '/auth/login')],
    children: [
      { path: 'dashboard',     loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'staff',         canActivate: [permissionGuard('staff.view', '/dashboard')], loadChildren:  () => import('./features/staff/staff.routes').then(m => m.STAFF_ROUTES) },

      { path: 'roles',         canActivate: [permissionGuard('roles.manage', '/dashboard')], canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/roles/roles.component').then(m => m.RolesComponent) },
      { path: 'leave',         canActivate: [permissionGuard('leave.approve', '/dashboard')], canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/leave/leave.component').then(m => m.LeaveComponent) },

      { path: 'payroll-locks',  canActivate: [permissionGuard('payroll.lock', '/dashboard')], loadComponent: () => import('./features/payroll-locks/payroll-locks.component').then(m => m.PayrollLocksComponent) },
      { path: 'cash-advances',  canActivate: [permissionGuard('cashadvance.manage', '/dashboard')], canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/cash-advances/cash-advances.component').then(m => m.CashAdvancesComponent) },
      { path: 'account',       loadChildren:  () => import('./features/account/account.routes').then(m => m.ACCOUNT_ROUTES) },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
