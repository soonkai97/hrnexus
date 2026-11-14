import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { DEFAULT_SCHEDULE, WorkSchedule } from '../utils/leave-hours';

export type CarryOverMode = 'none' | 'capped' | 'unlimited';

/** The company's HR rules (mirror of the API's CompanyPolicyDto). One policy per installation. */
export interface CompanyPolicy {
  /** ISO weekday numbers: 1 = Monday ... 7 = Sunday. */
  workingDays: number[];
  workdayHours: number;
  annualLeaveAdvanceDays: number;
  maxLeaveDays: number;
  carryOverMode: CarryOverMode;
  /** The cap used when the mode is 'capped'. */
  carryOverMaxHours: number;
  /** Daily pay rate divisor for unpaid-leave deductions in payroll. */
  unpaidLeaveDivisor: number;
  /** Counts saves; a save made from a stale copy is refused (409). */
  version: number;
}

/** What the server uses until a company saves its own policy (equals the rules the product shipped with). */
export const DEFAULT_POLICY: CompanyPolicy = {
  workingDays: [...DEFAULT_SCHEDULE.days],
  workdayHours: DEFAULT_SCHEDULE.hours,
  annualLeaveAdvanceDays: 3,
  maxLeaveDays: 30,
  carryOverMode: 'capped',
  carryOverMaxHours: 14,
  unpaidLeaveDivisor: 26,
  version: 0,
};

/**
 * The company policy. Every signed-in user reads it (leave forms preview hours with the working week);
 * only people with `policy.manage` can save it. Starts on the defaults and keeps them if loading fails,
 * so the forms keep working; the server stays the authority on every figure.
 */
@Injectable({ providedIn: 'root' })
export class CompanyPolicyService {
  private readonly http = inject(HttpClient);

  readonly #policy = signal<CompanyPolicy>(DEFAULT_POLICY);
  readonly #loaded = signal(false);

  readonly policy = this.#policy.asReadonly();
  /** True once the first load() has finished (success or failure). */
  readonly loaded = this.#loaded.asReadonly();
  /** The working week and day length, in the shape the leave-hours maths takes. */
  readonly schedule = computed<WorkSchedule>(() => ({
    days: this.#policy().workingDays,
    hours: this.#policy().workdayHours,
  }));

  /** Fetch the policy. Never errors: on failure the defaults stay. */
  load(): Observable<void> {
    return this.http.get<CompanyPolicy>('/api/company-policy').pipe(
      tap((p) => this.#policy.set(p)),
      map(() => void 0 as void),
      catchError(() => of(void 0 as void)),
      tap(() => this.#loaded.set(true)),
    );
  }

  /** Save the edited values with the version that was loaded; errors (400, 409) reach the caller. */
  save(values: Omit<CompanyPolicy, 'version'>): Observable<void> {
    const sent = this.#policy().version;
    return this.http
      .put<{ version: number }>('/api/company-policy', { ...values, version: sent })
      .pipe(
        tap((res) => this.#policy.set({ ...values, version: res.version })),
        map(() => void 0 as void),
      );
  }
}
