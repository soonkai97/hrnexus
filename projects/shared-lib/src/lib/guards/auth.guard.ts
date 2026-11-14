import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  // The access token now lives in an httpOnly cookie that JS can't read, so the guard can't
  // inspect its expiry — it gates on whether a user session is known locally. The server is the
  // real enforcer: if the cookie is expired, the first API call 401s and the jwtInterceptor
  // transparently refreshes (or logs out if the refresh cookie is also dead).
  if (auth.isAuthenticated()) return true;
  return router.createUrlTree(['/auth/login']);
};
