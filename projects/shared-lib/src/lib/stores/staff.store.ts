import { Injectable, inject, signal, computed } from '@angular/core';
import { tap } from 'rxjs';
import { StaffService } from '../services/staff.service';
import { Staff, RegisterStaffRequest, UpdateStaffRequest, StaffRole } from '../models';

@Injectable({ providedIn: 'root' })
export class StaffStore {
  private readonly svc = inject(StaffService);

  readonly #staff   = signal<Staff[]>([]);
  readonly #roles   = signal<StaffRole[]>([]);
  readonly #loading = signal(false);
  readonly #error   = signal('');

  readonly staff      = this.#staff.asReadonly();
  readonly roles      = this.#roles.asReadonly();
  readonly loading    = this.#loading.asReadonly();
  readonly error      = this.#error.asReadonly();
  readonly staffCount = computed(() => this.#staff().length);

  loadStaff(search?: string): void {
    this.#loading.set(true);
    this.svc.getAll(search).subscribe({
      next: list => { this.#staff.set(list); this.#loading.set(false); },
      error: ()   => { this.#error.set('Failed to load staff.'); this.#loading.set(false); },
    });
  }

  loadRoles(): void {
    this.svc.getRoles().subscribe({
      next: list => this.#roles.set(list),
      error: ()   => {},
    });
  }

  register(req: RegisterStaffRequest) {
    return this.svc.register(req).pipe(tap(() => this.loadStaff()));
  }

  update(id: string, req: UpdateStaffRequest) {
    return this.svc.update(id, req).pipe(
      tap(() => this.#staff.update(list =>
        list.map(s => s.id === id ? { ...s, ...req } : s)
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
    return this.svc.delete(id).pipe(
      tap(() => this.#staff.update(list => list.filter(s => s.id !== id)))
    );
  }
}
