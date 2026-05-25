import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { tap, map, catchError } from 'rxjs/operators';

export type FeatureKey =
  // Phase 1 — Finance
  | 'payroll'
  | 'tips'
  | 'tipsHistory'       // staff portal
  | 'paySummary'        // staff portal
  // Phase 2 — Communication
  | 'announcements'
  // Phase 3 — Staff Ledger
  | 'staffLedger';

export type FlagMap = Record<FeatureKey, boolean>;

const DEFAULTS: FlagMap = {
  // Phase 1
  payroll:        false,
  tips:           false,
  tipsHistory:    false,
  paySummary:     false,
  // Phase 2
  announcements:  false,
  // Phase 3
  staffLedger:  false,
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
}
