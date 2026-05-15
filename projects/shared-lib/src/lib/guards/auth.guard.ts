import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { UserRole } from '../models';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  // Check both that the user object exists AND the JWT hasn't expired.
  // Without the expiry check, an expired token passes the guard, the page
  // renders, fires every API call, and they all 401-storm before the
  // interceptor can redirect — noisy and confusing.
  if (auth.isAuthenticated() && auth.hasValidToken()) return true;
  return router.createUrlTree(['/auth/login']);
};

export const roleGuard = (...roles: UserRole[]): CanActivateFn => () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.hasRole(...roles)) return true;
  return router.createUrlTree(['/unauthorized']);
};
