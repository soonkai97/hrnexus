import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/** Shape of the JSON error body the API returns on failure, e.g. `{ "error": "..." }`. */
export interface ApiErrorBody {
  error?: string;
  message?: string;
}

/**
 * Extracts the API's curated, user-facing error message from any thrown HTTP error —
 * or `undefined` when the server didn't supply one (callers then fall back to a localized
 * generic message). Centralizes the `err.error.error` shape knowledge that used to be
 * duplicated (and loosely-typed) in every component's error callback.
 */
export function apiErrorMessage(err: unknown): string | undefined {
  const body = (err as { error?: unknown } | null | undefined)?.error;
  if (typeof body === 'string') return body || undefined;       // some endpoints return a raw string
  const typed = body as ApiErrorBody | null | undefined;
  return typed?.error ?? typed?.message;
}

/**
 * Centralized HTTP error logging. It does NOT alter the error on the wire — it rethrows the
 * original so existing handlers keep working — it just gives one place to observe failures and
 * the obvious home for future telemetry. Skips 401 (the jwt interceptor handles token refresh)
 * and 503 (maintenance redirect) to avoid noise.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 && err.status !== 503) {
        console.error(`[api] ${req.method} ${req.urlWithParams} -> ${err.status} ${apiErrorMessage(err) ?? err.message}`);
      }
      return throwError(() => err);
    }),
  );
