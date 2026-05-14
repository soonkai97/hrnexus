import { Routes } from '@angular/router';

export const PROFILE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./profile.component').then(m => m.ProfileComponent),
  },
  {
    path: 'change-password',
    loadComponent: () => import('./change-password-page.component').then(m => m.ChangePasswordPageComponent),
  },
];
