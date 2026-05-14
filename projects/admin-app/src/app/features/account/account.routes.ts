import { Routes } from '@angular/router';

export const ACCOUNT_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./account.component').then(m => m.AccountComponent) },
  { path: 'change-password', loadComponent: () => import('./change-password-page.component').then(m => m.ChangePasswordPageComponent) },
];
