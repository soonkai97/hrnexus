import { Routes } from '@angular/router';
import { authGuard } from 'shared-lib';
import { ShellComponent } from './shell/shell.component';

export const routes: Routes = [
  { path: '', redirectTo: 'log-shift', pathMatch: 'full' },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then(m => m.AUTH_ROUTES),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: 'log-shift',     loadComponent: () => import('./features/log-shift/log-shift.component').then(m => m.LogShiftComponent) },
      { path: 'pay-summary',   loadComponent: () => import('./features/pay-summary/pay-summary.component').then(m => m.PaySummaryComponent) },
      { path: 'profile',       loadChildren:  () => import('./features/profile/profile.routes').then(m => m.PROFILE_ROUTES) },
      { path: 'my-leave',      loadComponent: () => import('./features/my-leave/my-leave.component').then(m => m.MyLeaveComponent) },
      { path: 'messaging',     loadComponent: () => import('./features/messaging/messaging.component').then(m => m.MessagingComponent) },
      { path: 'tips-history',    loadComponent: () => import('./features/tips-history/tips-history.component').then(m => m.TipsHistoryComponent) },
      { path: 'announcements',   loadComponent: () => import('./features/announcements/announcements.component').then(m => m.AnnouncementsComponent) },
    ],
  },
  { path: '**', redirectTo: 'log-shift' },
];
