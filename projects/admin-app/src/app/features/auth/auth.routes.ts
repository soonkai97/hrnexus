import { Routes } from '@angular/router';

export const AUTH_ROUTES: Routes = [
  { path: 'login',          loadComponent: () => import('./login/login.component').then(m => m.AdminLoginComponent) },
  { path: 'forgot-password', loadComponent: () => import('./forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'set-password',    loadComponent: () => import('./set-password/set-password.component').then(m => m.SetPasswordComponent) },
  { path: '', redirectTo: 'login', pathMatch: 'full' },
];
