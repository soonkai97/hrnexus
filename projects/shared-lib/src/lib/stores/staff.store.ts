import { Injectable, inject, signal } from '@angular/core';
import { Observable, Subject, EMPTY } from 'rxjs';
import { catchError, map, switchMap, tap } from 'rxjs/operators';
import { StaffManagementService } from '../services/staff-management.service';
import { ToastService } from '../services/toast.service';
import { apiErrorMessage } from '../auth/error.interceptor';
import { Staff, RegisterStaffRequest, UpdateStaffRequest, StaffRole } from '../models';

@Injectable({ providedIn: 'root' })
export class StaffStore {
  private readonly svc = inject(StaffManagementService);
  private readonly toast = inject(ToastService);

  readonly #staff      = signal<Staff[]>([]);
  readonly #roles      = signal<StaffRole[]>([]);
  readonly #loading    = signal(false);
  readonly #error      = signal('');
  readonly #totalCount = signal(0);

  readonly staff      = this.#staff.asReadonly();
  readonly roles      = this.#roles.asReadonly();
  readonly loading    = this.#loading.asReadonly();
  readonly error      = this.#error.asReadonly();
  readonly totalCount = this.#totalCount.asReadonly();

  // switchMap cancels any in-flight request when a newer loadStaff() call arrives —
  // prevents stale page data appearing after rapid pagination or search changes.
  private readonly _loadTrigger$ = new Subject<{ search?: string; page: number; pageSize: number; includeHidden: boolean }>();

  constructor() {
    this._loadTrigger$.pipe(
      switchMap(({ search, page, pageSize, includeHidden }) => {
        this.#loading.set(true);
        return this.svc.getAll(search, page, pageSize, includeHidden).pipe(
          catchError(() => {
            this.#error.set('Failed to load staff.');
            this.#loading.set(false);
            return EMPTY; // keep the outer stream alive for future calls
          }),
        );
      }),
    ).subscribe(result => {
      this.#staff.set(result.items);
      this.#totalCount.set(result.total);
      this.#loading.set(false);
    });
  }

  loadStaff(search?: string, page = 1, pageSize = 10, includeHidden = false): void {
    this._loadTrigger$.next({ search, page, pageSize, includeHidden });
  }

  /** Show/hide a staff account in the roster. Presentational only — caller reloads the list after,
   *  since hiding a row removes it from the default view. */
  setHidden(id: string, hidden: boolean) {
    return this.svc.setHidden(id, hidden);
  }

  private rolesLoading = false;

  loadRoles(): void {
    if (this.rolesLoading) return; // dedup: skip if a fetch is already in flight
    this.rolesLoading = true;
    this.svc.getRoles().subscribe({
      next: list => { this.#roles.set(list); this.rolesLoading = false; },
      error: ()   => { this.rolesLoading = false; },
    });
  }

  register(req: RegisterStaffRequest) {
    return this.svc.register(req);
  }

  update(id: string, req: UpdateStaffRequest) {
    return this.svc.update(id, req).pipe(
      tap(() => this.#staff.update(list =>
        list.map(s => s.id === id ? { ...s, ...req, email: req.email ?? s.email } : s)
      ))
    );
  }

  toggleActive(id: string, currentlyActive: boolean) {
    return this.svc.toggleActive(id, !currentlyActive).pipe(
      tap(() => this.#staff.update(list =>
        list.map(s => s.id === id ? { ...s, isActive: !currentlyActive } : s)
      ))
    );
  }

  createRole(name: string) {
    return this.svc.createRole(name).pipe(tap(() => this.loadRoles()));
  }

  renameRole(id: string, name: string) {
    return this.svc.renameRole(id, name).pipe(
      tap(() => this.#roles.update(list => list.map(r => r.id === id ? { ...r, name } : r)))
    );
  }

  deleteRole(id: string) {
    return this.svc.deleteRole(id).pipe(
      tap(() => this.#roles.update(list => list.filter(r => r.id !== id)))
    );
  }

  resetPassword(id: string, newPassword: string) {
    return this.svc.resetPassword(id, newPassword);
  }

  /** Who a person reports to (null clears it). Decides whose pay a manager may read and sign off. */
  setReportsTo(id: string, managerId: string | null): Observable<void> {
    return this.svc.setReportsTo(id, managerId).pipe(
      tap(() => this.toast.success('Saved.')),
      catchError((e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to save.');
        return EMPTY; // the reason is shown; the caller only hears about success
      }),
    );
  }

  /** Searches the staff list on the server (a manager picker works however many people there are). */
  findPeople(search: string, pageSize = 20): Observable<Staff[]> {
    return this.svc.getAll(search, 1, pageSize).pipe(
      map((page) => page.items),
      catchError(() => {
        this.toast.error('Failed to search the staff list.');
        return EMPTY; // a failed search must not end a caller's search stream
      }),
    );
  }

  deleteStaff(id: string) {
    return this.svc.delete(id);
  }

  // ── Bulk actions ────────────────────────────────────────────────────────────
  bulkAssignRoles(staffIds: string[], roles: string[]) {
    return this.svc.bulkAssignRoles(staffIds, roles);
  }

  bulkDeactivate(staffIds: string[]) {
    return this.svc.bulkDeactivate(staffIds);
  }
}
