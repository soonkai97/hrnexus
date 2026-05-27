import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/**
 * Detects HTTP 503 (maintenance mode) and redirects to /maintenance.html.
 *
 * Why /maintenance.html and not /?
 * The Angular service worker intercepts all navigation requests to the app
 * scope and serves the cached app shell. URLs with a file extension (.html)
 * are excluded from navigation URL matching in ngsw-config, so the browser
 * fetches /maintenance.html directly from nginx — bypassing the SW cache —
 * and the maintenance page is shown immediately.
 */
export const maintenanceInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 503) {
        window.location.href = '/maintenance.html';
      }
      return throwError(() => err);
    }),
  );
