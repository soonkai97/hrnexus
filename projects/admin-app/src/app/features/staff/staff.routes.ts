import { Routes } from '@angular/router';
import { unsavedChangesGuard } from 'shared-lib';

export const STAFF_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./staff.component').then(m => m.StaffComponent) },
  { path: ':id', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./staff-detail.component').then(m => m.StaffDetailComponent) },
];
