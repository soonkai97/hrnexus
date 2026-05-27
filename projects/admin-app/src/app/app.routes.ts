import { Routes } from '@angular/router';
import { authGuard, roleGuard, featureFlagGuard, unsavedChangesGuard } from 'shared-lib';
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
    canActivate: [authGuard, roleGuard('manager', 'admin')],
    children: [
      { path: 'dashboard',     loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'staff',         loadChildren:  () => import('./features/staff/staff.routes').then(m => m.STAFF_ROUTES) },

      { path: 'reports',       loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent) },
      { path: 'roles',         canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/roles/roles.component').then(m => m.RolesComponent) },
      { path: 'locations',     canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/locations/locations.component').then(m => m.LocationsComponent) },
      { path: 'leave',         canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/leave/leave.component').then(m => m.LeaveComponent) },

      { path: 'announcements', canActivate: [featureFlagGuard('announcements',  '/dashboard')], canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/announcements/announcements.component').then(m => m.AnnouncementsComponent) },
      { path: 'tips',          canActivate: [featureFlagGuard('tips',           '/dashboard')], canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/tips/tips.component').then(m => m.TipsComponent) },
      { path: 'payroll',         canActivate: [featureFlagGuard('payroll',        '/dashboard')], loadComponent: () => import('./features/payroll/payroll.component').then(m => m.PayrollComponent) },
      { path: 'staff-ledger',   canActivate: [featureFlagGuard('staffLedger', '/dashboard')], canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/staff-ledger/staff-ledger.component').then(m => m.StaffLedgerComponent) },
      { path: 'staff-settings', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/staff-settings/staff-settings.component').then(m => m.StaffSettingsComponent) },
      { path: 'account',       loadChildren:  () => import('./features/account/account.routes').then(m => m.ACCOUNT_ROUTES) },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
