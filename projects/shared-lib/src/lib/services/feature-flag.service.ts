import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { tap, map, catchError } from 'rxjs/operators';

export type FeatureKey =
  // Phase 1 — Operations
  | 'otRules'
  // Phase 2 — Finance
  | 'payroll'
  | 'tips'
  | 'tipsHistory'       // staff portal
  | 'paySummary'        // staff portal
  // Phase 3 — Communication
  | 'messagingAdmin'
  | 'announcements'
  | 'messagingStaff';   // staff portal

export type FlagMap = Record<FeatureKey, boolean>;

const DEFAULTS: FlagMap = {
  // Phase 1
  otRules:        false,
  // Phase 2
  payroll:        false,
  tips:           false,
  tipsHistory:    false,
  paySummary:     false,
  // Phase 3
  messagingAdmin: false,
  announcements:  false,
  messagingStaff: false,
};

@Injectable({ providedIn: 'root' })
export class FeatureFlagService {
  private readonly http = inject(HttpClient);

  readonly #flags  = signal<FlagMap>({ ...DEFAULTS });
  readonly #loaded = signal(false);

  readonly flags  = this.#flags.asReadonly();
  /** True once the first load() response has arrived (success or failure). */
  readonly loaded = this.#loaded.asReadonly();

  /** Load flags from backend. Called by each shell on mount. Fails silently. */
  load(): Observable<void> {
    return this.http.get<Partial<FlagMap>>('/api/feature-flags').pipe(
      tap(data => { this.#flags.set({ ...DEFAULTS, ...data }); this.#loaded.set(true); }),
      map(() => void 0 as void),
      catchError(() => { this.#loaded.set(true); return of(void 0 as void); }),
    );
  }

  isEnabled(key: FeatureKey): boolean {
    return this.#flags()[key] ?? false;
  }

  /** Toggle one flag and persist (admin only). Optimistic — reverts on error. */
  toggle(key: FeatureKey): Observable<void> {
    const prev = { ...this.#flags() };
    const next = { ...prev, [key]: !prev[key] };
    this.#flags.set(next);
    return this.http.put<void>('/api/feature-flags', next, { headers: this.accessHeaders() }).pipe(
      catchError(err => { this.#flags.set(prev); return throwError(() => err); }),
    );
  }

  enableAll(): Observable<void> {
    const all  = Object.fromEntries(Object.keys(DEFAULTS).map(k => [k, true])) as FlagMap;
    const prev = { ...this.#flags() };
    this.#flags.set(all);
    return this.http.put<void>('/api/feature-flags', all, { headers: this.accessHeaders() }).pipe(
      catchError(err => { this.#flags.set(prev); return throwError(() => err); }),
    );
  }

  disableAll(): Observable<void> {
    const none = { ...DEFAULTS };
    const prev = { ...this.#flags() };
    this.#flags.set(none);
    return this.http.put<void>('/api/feature-flags', none, { headers: this.accessHeaders() }).pipe(
      catchError(err => { this.#flags.set(prev); return throwError(() => err); }),
    );
  }

  /** Reads the feature-access unlock token from sessionStorage (set by FeaturePinService). */
  private accessHeaders(): HttpHeaders {
    const token = sessionStorage.getItem('nc_feature_token');
    return token ? new HttpHeaders({ 'X-Feature-Access': token }) : new HttpHeaders();
  }
}
