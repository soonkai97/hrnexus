import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { tap, map, catchError } from 'rxjs/operators';

/**
 * Effective permission keys (mirror of the backend AppConstants.Permissions catalog).
 * Access is gated by these, resolved per-user from role defaults + per-user overrides.
 */
export type PermissionKey =
  | 'staff.view'   | 'staff.manage'
  | 'leave.approve' | 'leave.manage' | 'policy.manage'
  | 'reports.view'
  | 'payroll.view' | 'payroll.manage' | 'payroll.lock'
  | 'roles.manage'
  | 'cashadvance.manage';

/**
 * Loads the signed-in user's effective permissions from the API and answers has(key).
 * Mirrors FeatureFlagService: signal-backed, loaded once per session, fails silently to empty.
 */
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly http = inject(HttpClient);

  readonly #perms  = signal<ReadonlySet<string>>(new Set<string>());
  readonly #loaded = signal(false);

  /** True once the first load() response has arrived (success or failure). */
  readonly loaded = this.#loaded.asReadonly();

  /** Fetch the caller's effective permissions. Called by the shell + guards. Fails silently. */
  load(): Observable<void> {
    return this.http.get<string[]>('/api/auth/me/permissions').pipe(
      tap(keys => { this.#perms.set(new Set(keys)); this.#loaded.set(true); }),
      map(() => void 0 as void),
      catchError(() => { this.#loaded.set(true); return of(void 0 as void); }),
    );
  }

  has(key: PermissionKey): boolean {
    return this.#perms().has(key);
  }

  /** Clear on logout so the next user doesn't inherit cached permissions. */
  clear(): void {
    this.#perms.set(new Set<string>());
    this.#loaded.set(false);
  }
}
