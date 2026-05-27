import { Injectable, inject, signal } from '@angular/core';
import { Subject, EMPTY } from 'rxjs';
import { catchError, switchMap, tap } from 'rxjs/operators';
import { StaffManagementService } from '../services/staff-management.service';
import { Staff, RegisterStaffRequest, UpdateStaffRequest, StaffRole } from '../models';

@Injectable({ providedIn: 'root' })
export class StaffStore {
  private readonly svc = inject(StaffManagementService);

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
  private readonly _loadTrigger$ = new Subject<{ search?: string; page: number; pageSize: number }>();

  constructor() {
    this._loadTrigger$.pipe(
      switchMap(({ search, page, pageSize }) => {
        this.#loading.set(true);
        return this.svc.getAll(search, page, pageSize).pipe(
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

  loadStaff(search?: string, page = 1, pageSize = 10): void {
    this._loadTrigger$.next({ search, page, pageSize });
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

  createRole(name: string, hourlyRate: number = 0) {
    return this.svc.createRole(name, hourlyRate).pipe(tap(() => this.loadRoles()));
  }

  updateRoleRate(name: string, hourlyRate: number) {
    return this.svc.updateRoleRate(name, hourlyRate).pipe(
      tap(() => this.#roles.update(list =>
        list.map(r => r.name === name ? { ...r, hourlyRate } : r)
      ))
    );
  }

  deleteRole(name: string) {
    return this.svc.deleteRole(name).pipe(
      tap(() => this.#roles.update(list => list.filter(r => r.name !== name)))
    );
  }

  resetPassword(id: string, newPassword: string) {
    return this.svc.resetPassword(id, newPassword);
  }

  deleteStaff(id: string) {
    return this.svc.delete(id);
  }
}
