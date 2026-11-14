import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, switchMap, tap } from 'rxjs/operators';
import { LeaveColour, LeaveTypeCatalogue, LeaveTypeDefinition } from '../models/leave.model';
import { WorkSchedule } from '../utils/leave-hours';

/** Every weekday, for a leave type that counts calendar days. */
const ALL_DAYS: readonly number[] = [1, 2, 3, 4, 5, 6, 7];

const base = (
  key: string,
  label: string,
  colour: LeaveColour,
  sortOrder: number,
  changes: Partial<LeaveTypeDefinition>,
): LeaveTypeDefinition => ({
  key,
  label,
  colour,
  sortOrder,
  active: true,
  isSystem: false,
  pool: 'none',
  allowanceDays: null,
  maxDaysPerRequest: null,
  autoApprove: false,
  advanceNoticeDays: null,
  advanceNoticeFromPolicy: false,
  deductsPay: false,
  adminOnly: false,
  countsCalendarDays: false,
  sensitive: false,
  hasLeave: false,
  ...changes,
});

/**
 * The types an untouched installation has (mirror of the API's LeaveTypeDefaults), used until the catalogue
 * loads and if loading fails, so the leave forms always work. The server stays the authority on every rule.
 */
export const DEFAULT_LEAVE_TYPES: readonly LeaveTypeDefinition[] = [
  base('annual', 'Annual', 'success', 10, { pool: 'annual', advanceNoticeFromPolicy: true }),
  base('emergency', 'Emergency', 'warn', 20, { pool: 'annual', autoApprove: true }),
  base('sick', 'Sick', 'danger', 30, { pool: 'sick', autoApprove: true, isSystem: true }),
  base('hospitalisation', 'Hospitalisation', 'danger', 40, {
    pool: 'own',
    allowanceDays: 60,
    sensitive: true,
  }),
  base('maternity', 'Maternity', 'info', 50, {
    maxDaysPerRequest: 98,
    countsCalendarDays: true,
    sensitive: true,
  }),
  base('paternity', 'Paternity', 'info', 60, {
    maxDaysPerRequest: 7,
    countsCalendarDays: true,
    sensitive: true,
  }),
  base('compassionate', 'Compassionate', 'contrast', 70, { maxDaysPerRequest: 3, sensitive: true }),
  base('unpaid', 'Unpaid', 'secondary', 80, { deductsPay: true }),
  base('no-show', 'No-show', 'warn', 90, { deductsPay: true, adminOnly: true, isSystem: true }),
];

/**
 * The company's leave types. Everyone signed in reads them (the leave forms, tags, balances and notice rules
 * are built from the catalogue, never from fixed names); only `policy.manage` saves them. Starts on the
 * built-in types and keeps them if loading fails.
 */
@Injectable({ providedIn: 'root' })
export class LeaveTypeService {
  private readonly http = inject(HttpClient);

  readonly #types = signal<LeaveTypeDefinition[]>([...DEFAULT_LEAVE_TYPES]);
  readonly #loaded = signal(false);

  /** Every type in sort order, active or not (a deactivated type keeps its name for old leave). */
  readonly types = this.#types.asReadonly();
  /** True once the first load() has finished (success or failure). */
  readonly loaded = this.#loaded.asReadonly();
  /** The types staff can apply for: active and not recorded by an administrator. */
  readonly applicable = computed(() => this.#types().filter((t) => t.active && !t.adminOnly));
  /** The types still taking new requests (an administrator can also record the admin-only ones). */
  readonly active = computed(() => this.#types().filter((t) => t.active));

  find(key: string | null | undefined): LeaveTypeDefinition | undefined {
    return key ? this.#types().find((t) => t.key === key.toLowerCase()) : undefined;
  }

  /** The type's name, or the stored key when the catalogue does not have it. */
  label(key: string): string {
    return this.find(key)?.label ?? key;
  }

  /** The tag severity for a type; neutral for an unknown key. */
  severity(key: string): LeaveColour {
    return this.find(key)?.colour ?? 'secondary';
  }

  /**
   * The schedule a type counts its days by: the company's working days, or every day of the week (at the
   * company's hours per day) for a calendar-day type. Same rule as the server.
   */
  scheduleFor(key: string, company: WorkSchedule): WorkSchedule {
    return this.find(key)?.countsCalendarDays ? { days: ALL_DAYS, hours: company.hours } : company;
  }

  /** Days of notice a type needs: the company's annual-leave notice, its own days, or null for none. */
  noticeDays(type: LeaveTypeDefinition, policyAdvanceDays: number): number | null {
    return type.advanceNoticeFromPolicy ? policyAdvanceDays : type.advanceNoticeDays;
  }

  /** The longest single request in calendar days: the type's own limit, or the company policy's. */
  maxDays(key: string, policyMaxDays: number): number {
    return this.find(key)?.maxDaysPerRequest ?? policyMaxDays;
  }

  /** Fetch the catalogue. Never errors: on failure the built-in types stay. */
  load(): Observable<void> {
    return this.http.get<LeaveTypeCatalogue>('/api/leave-types').pipe(
      tap((c) => this.#types.set([...c.types].sort((a, b) => a.sortOrder - b.sortOrder))),
      map(() => void 0 as void),
      catchError(() => of(void 0 as void)),
      tap(() => this.#loaded.set(true)),
    );
  }

  /** The settings page: every type with which already have leave, and the version to save against. */
  loadAll(): Observable<LeaveTypeCatalogue> {
    return this.http.get<LeaveTypeCatalogue>('/api/leave-types/all');
  }

  /**
   * Replace the whole catalogue (add, edit, deactivate and drop are one save). Errors reach the caller: 400
   * for an invalid type, 409 when the version is stale. On success the forms are rebuilt from the server.
   * @returns the new version
   */
  save(version: number, types: LeaveTypeDefinition[]): Observable<number> {
    return this.http.put<{ version: number }>('/api/leave-types', { version, types }).pipe(
      switchMap((res) => this.load().pipe(map(() => res.version))),
    );
  }
}
