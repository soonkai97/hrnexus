import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs/operators';
import { PermissionService, PermissionKey } from '../services/permission.service';

/**
 * Blocks a route unless the user holds the given permission. Mirrors featureFlagGuard:
 * on a fresh page load (permissions not yet loaded) it triggers the load itself, since guards
 * resolve before the shell component initialises.
 *
 * Usage:
 *   canActivate: [authGuard, permissionGuard('portal.admin', '/auth/login')]
 *   canActivate: [permissionGuard('staff.view', '/dashboard')]
 */
export const permissionGuard = (key: PermissionKey, fallback = '/dashboard'): CanActivateFn => () => {
  const perms  = inject(PermissionService);
  const router = inject(Router);

  const decide = () => perms.has(key) ? true : router.createUrlTree([fallback]);

  if (perms.loaded()) return decide();
  return perms.load().pipe(map(decide));
};
