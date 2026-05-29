import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { StaffDetail, UpdateStaffRequest, StaffStore, StaffManagementService, AuthService, ToastService, ConfirmService, WorkLogService, WorkLogDto, AmPmPipe, TrimInputDirective, CanComponentDeactivate } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { MultiSelect } from 'primeng/multiselect';
import { Paginator } from 'primeng/paginator';

@Component({
  selector: 'app-staff-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, TranslatePipe, AmPmPipe, TrimInputDirective,
    Button, InputText, TableModule, Tag, Card, Skeleton, DatePicker, MultiSelect, Paginator],
  templateUrl: './staff-detail.component.html',
  styleUrl: './staff-detail.component.scss',
})
export class StaffDetailComponent implements OnInit, CanComponentDeactivate {
  readonly staffStore  = inject(StaffStore);
  private readonly auth        = inject(AuthService);
  private readonly staffSvc    = inject(StaffManagementService);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly route       = inject(ActivatedRoute);
  private readonly workLogSvc  = inject(WorkLogService);

  staff            = signal<StaffDetail | null>(null);
  loading          = signal(true);
  editMode         = signal(false);
  saving           = signal(false);

  /** True when the viewer is a manager (not an admin) — used to hide role editing. */
  readonly isManager = computed(() => this.auth.hasRole('manager') && !this.auth.hasRole('admin'));

  availableRoles = computed(() => this.staffStore.roles().filter(r => r.name !== 'admin'));

  workLogs        = signal<WorkLogDto[]>([]);
  workLogsLoading = signal(false);
  totalLogs       = signal(0);
  currentPage     = signal(1);
  readonly pageSize = 10;
  totalPages = computed(() => Math.max(1, Math.ceil(this.totalLogs() / this.pageSize)));

  editForm: UpdateStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: null, roles: [],
    phoneNumber: null, icNo: null, passportNo: null, dateOfBirth: null,
  };
  private _initialEditForm: UpdateStaffRequest = { ...this.editForm, roles: [] };

  private staffId = '';

  ngOnInit(): void {
    this.staffId = this.route.snapshot.paramMap.get('id') ?? '';
    this.staffStore.loadRoles();
    this.loadStaff();
    this.loadWorkLogs(1);
  }

  loadStaff(): void {
    this.loading.set(true);
    this.staffSvc.getById(this.staffId).subscribe({
      next:  d  => { this.staff.set(d); this.loading.set(false); },
      error: () => { this.toast.error('Failed to load staff details.'); this.loading.set(false); },
    });
  }

  loadWorkLogs(page: number): void {
    this.workLogsLoading.set(true);
    this.currentPage.set(page);
    this.workLogSvc.getAll({ employeeId: this.staffId, page, pageSize: this.pageSize }).subscribe({
      next:  result => {
        this.workLogs.set(result.items);
        this.totalLogs.set(result.total);
        this.workLogsLoading.set(false);
      },
      error: () => this.workLogsLoading.set(false),
    });
  }

  startEdit(): void {
    const s = this.staff()!;
    this.editForm = {
      staffId: s.staffId, firstName: s.firstName, lastName: s.lastName,
      email: s.email || null,
      roles: [...s.roles], phoneNumber: s.phoneNumber,
      icNo: s.icNo ?? null, passportNo: s.passportNo ?? null,
      dateOfBirth: s.dateOfBirth ?? null,
    };
    this._initialEditForm = { ...this.editForm, roles: [...this.editForm.roles] };
    this.touched.set(new Set());
    this.editMode.set(true);
  }

  hasUnsavedChanges(): boolean {
    if (!this.editMode()) return false;
    return JSON.stringify(this.editForm) !== JSON.stringify(this._initialEditForm);
  }

  /** Gates the Save button — only Staff ID and first name are required (email must be valid if present). */
  get isEditFormValid(): boolean {
    return !!this.editForm.staffId?.trim() &&
      !!this.editForm.firstName?.trim() &&
      this.emailIsValid();
  }

  // ── Per-field inline validation ───────────────────────────────────────────────
  private readonly emailRegex = /^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$/;
  readonly touched = signal<Set<string>>(new Set());

  private emailIsValid(): boolean {
    const e = this.editForm.email?.trim();
    return !e || this.emailRegex.test(e);
  }

  markTouched(field: string): void {
    this.touched.update(s => new Set(s).add(field));
  }

  /** Inline error text for a field — empty when valid or not yet blurred. */
  fieldError(field: 'staffId' | 'firstName' | 'email'): string {
    if (!this.touched().has(field)) return '';
    const f = this.editForm;
    switch (field) {
      case 'staffId':   return f.staffId?.trim()   ? '' : 'Staff ID is required.';
      case 'firstName': return f.firstName?.trim() ? '' : 'First name is required.';
      case 'email':     return this.emailIsValid() ? '' : 'Enter a valid email address.';
    }
  }

  cancelEdit(): void {
    if (this.hasUnsavedChanges()) {
      this.confirmSvc.confirm({
        title: 'Discard Changes',
        message: 'You have unsaved changes. Discard them?',
        confirmLabel: 'Discard',
        danger: true,
      }).subscribe(ok => { if (ok) this.editMode.set(false); });
    } else {
      this.editMode.set(false);
    }
  }

  saveEdit(): void {
    this.saving.set(true);
    // Coerce empty-string DOB to null — the API field is DateTime? and rejects "".
    const req: UpdateStaffRequest = {
      ...this.editForm,
      dateOfBirth: this.editForm.dateOfBirth?.trim() || null,
    };
    this.staffStore.update(this.staffId, req).subscribe({
      next:  ()       => { this.loadStaff(); this.editMode.set(false); this.saving.set(false); this.toast.success('Profile updated.'); },
      error: (e: any) => { this.toast.error(e?.error?.error ?? 'Update failed.'); this.saving.set(false); },
    });
  }
}
