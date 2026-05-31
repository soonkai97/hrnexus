import { Routes } from '@angular/router';
import { authGuard, featureFlagGuard, unsavedChangesGuard } from 'shared-lib';
import { ShellComponent } from './shell/shell.component';

export const routes: Routes = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then(m => m.AUTH_ROUTES),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: 'home',          loadComponent: () => import('./features/home/home.component').then(m => m.HomeComponent) },
      { path: 'log-shift',     canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/log-shift/log-shift.component').then(m => m.LogShiftComponent) },
      { path: 'pay-summary',   canActivate: [featureFlagGuard('paySummary',     '/home')], loadComponent: () => import('./features/pay-summary/pay-summary.component').then(m => m.PaySummaryComponent) },
      { path: 'profile',       loadChildren:  () => import('./features/profile/profile.routes').then(m => m.PROFILE_ROUTES) },
      { path: 'my-leave',      canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/my-leave/my-leave.component').then(m => m.MyLeaveComponent) },
    ],
  },
  { path: '**', redirectTo: 'home' },
];
