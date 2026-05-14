import { Routes } from '@angular/router';

export const STAFF_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./staff.component').then(m => m.StaffComponent) },
  { path: ':id', loadComponent: () => import('./staff-detail.component').then(m => m.StaffDetailComponent) },
];
