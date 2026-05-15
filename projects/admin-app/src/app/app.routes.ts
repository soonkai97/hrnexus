import { Routes } from '@angular/router';
import { authGuard, roleGuard, featureFlagGuard } from 'shared-lib';
import { ShellComponent } from './shell/shell.component';
import { featurePinGuard } from './features/feature-flags/feature-pin.guard';

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
      { path: 'roles',         loadComponent: () => import('./features/roles/roles.component').then(m => m.RolesComponent) },
      { path: 'locations',     loadComponent: () => import('./features/locations/locations.component').then(m => m.LocationsComponent) },
      { path: 'leave',         loadComponent: () => import('./features/leave/leave.component').then(m => m.LeaveComponent) },

      { path: 'overtime',      canActivate: [featureFlagGuard('otRules',        '/dashboard')], loadComponent: () => import('./features/overtime/overtime.component').then(m => m.OvertimeComponent) },
      { path: 'announcements', canActivate: [featureFlagGuard('announcements',  '/dashboard')], loadComponent: () => import('./features/announcements/announcements.component').then(m => m.AnnouncementsComponent) },
      { path: 'tips',          canActivate: [featureFlagGuard('tips',           '/dashboard')], loadComponent: () => import('./features/tips/tips.component').then(m => m.TipsComponent) },
      { path: 'payroll',       canActivate: [featureFlagGuard('payroll',        '/dashboard')], loadComponent: () => import('./features/payroll/payroll.component').then(m => m.PayrollComponent) },
      { path: 'notifications', loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent) },
      { path: 'messaging',     canActivate: [featureFlagGuard('messagingAdmin', '/dashboard')], loadComponent: () => import('./features/messaging/messaging.component').then(m => m.MessagingComponent) },
      {
        path: 'features',
        canActivate: [roleGuard('admin')],
        children: [
          { path: '',       canActivate: [featurePinGuard], loadComponent: () => import('./features/feature-flags/feature-flags.component').then(m => m.FeatureFlagsComponent) },
          { path: 'unlock', loadComponent: () => import('./features/feature-flags/unlock/feature-pin-entry.component').then(m => m.FeaturePinEntryComponent) },
        ],
      },
      { path: 'account',       loadChildren:  () => import('./features/account/account.routes').then(m => m.ACCOUNT_ROUTES) },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
