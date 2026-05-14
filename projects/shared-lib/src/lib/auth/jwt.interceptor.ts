import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import {
  BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError,
} from 'rxjs';
import { AuthService } from './auth.service';
import { AuthResponse } from '../models';

// ── Shared refresh state (module-level, so it's a singleton across all calls) ─

/** null = not refreshing; Observable = refresh in-flight */
let refreshing$: Observable<AuthResponse> | null = null;

/** Emits the new AuthResponse once the refresh completes; null while idle. */
const refreshDone$ = new BehaviorSubject<AuthResponse | null>(null);

// ─────────────────────────────────────────────────────────────────────────────

export const jwtInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const auth = inject(AuthService);

  // Attach the current access token to every outbound request
  const authReq = withBearer(req, auth.getAccessToken());

  return next(authReq).pipe(
    catchError((err: HttpErrorResponse) => {
      // Only intercept 401s that aren't from auth endpoints (avoid refresh loops)
      if (err.status !== 401 || req.url.includes('/api/auth/')) {
        return throwError(() => err);
      }

      // ── If a refresh is already in-flight, wait for it then retry ──────────
      if (refreshing$) {
        return refreshDone$.pipe(
          filter(res => res !== null),
          take(1),
          switchMap(res => next(withBearer(req, res!.accessToken))),
        );
      }

      // ── Start a new refresh ─────────────────────────────────────────────────
      refreshDone$.next(null);           // reset "done" state
      refreshing$ = auth.refreshToken();

      return refreshing$.pipe(
        switchMap(res => {
          refreshing$ = null;
          refreshDone$.next(res);        // unblock any waiting requests
          return next(withBearer(req, res.accessToken));
        }),
        catchError(refreshErr => {
          refreshing$ = null;
          refreshDone$.next(null);
          // refreshToken() already called logout() internally
          return throwError(() => refreshErr);
        }),
      );
    }),
  );
};

function withBearer(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;
}
