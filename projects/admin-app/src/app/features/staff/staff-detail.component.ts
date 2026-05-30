import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { StaffDetail, UpdateStaffRequest, StaffStore, StaffManagementService, ToastService, ConfirmService, WorkLogService, WorkLogDto, AmPmPipe, TrimInputDirective, CanComponentDeactivate, apiErrorMessage, PermissionAdminService, StaffOverride } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { MultiSelect } from 'primeng/multiselect';
import { Paginator } from 'primeng/paginator';
import { SelectButton } from 'primeng/selectbutton';

/** Tri-state choice for a per-user permission override. */
type OverrideChoice = 'inherit' | 'grant' | 'revoke';

@Component({
  selector: 'app-staff-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, TranslatePipe, AmPmPipe, TrimInputDirective,
    Button, InputText, TableModule, Tag, Card, Skeleton, DatePicker, MultiSelect, Paginator, SelectButton],
  templateUrl: './staff-detail.component.html',
  styleUrl: './staff-detail.component.scss',
})
export class StaffDetailComponent implements OnInit, CanComponentDeactivate {
  readonly staffStore  = inject(StaffStore);
  private readonly staffSvc    = inject(StaffManagementService);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly route       = inject(ActivatedRoute);
  private readonly workLogSvc  = inject(WorkLogService);
  private readonly permSvc     = inject(PermissionAdminService);
  private readonly translate   = inject(TranslateService);

  staff            = signal<StaffDetail | null>(null);
  loading          = signal(true);
  editMode         = signal(false);
  saving           = signal(false);

  /** Managers now have the same power as admins, so role editing is never hidden. */
  readonly isManager = computed(() => false);

  // Every defined role is assignable (incl. SUPERADMIN); managers and admins have equal power.
  availableRoles = computed(() => this.staffStore.roles());

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

  // ── Per-user permission overrides ───────────────────────────────────────────
  permKeys     = signal<string[]>([]);
  /** permissionKey → tri-state choice; defaults to 'inherit' for every catalog key. */
  permChoices  = signal<Record<string, OverrideChoice>>({});
  permLoading  = signal(true);
  permSaving   = signal(false);
  permEditMode = signal(false);
  /** Snapshot of choices taken on load / entering edit — basis for dirty detection + Cancel. */
  permInitial  = signal<Record<string, OverrideChoice>>({});

  /** Save is enabled only when at least one choice differs from the snapshot. */
  readonly permDirty = computed(() => {
    const cur = this.permChoices(), init = this.permInitial();
    return this.permKeys().some(k => (cur[k] ?? 'inherit') !== (init[k] ?? 'inherit'));
  });
  /** Keys with a non-inherit choice — what the read-only view lists. */
  readonly activeOverrides = computed(() =>
    this.permKeys().filter(k => (this.permChoices()[k] ?? 'inherit') !== 'inherit'));

  /** Options for each tri-state p-selectButton (labels resolved at runtime via translate). */
  readonly permOptions = computed<{ label: string; value: OverrideChoice }[]>(() => [
    { label: this.translate.instant('permissions.inherit'), value: 'inherit' },
    { label: this.translate.instant('permissions.grant'),   value: 'grant'   },
    { label: this.translate.instant('permissions.revoke'),  value: 'revoke'  },
  ]);

  permChoiceLabel(c: OverrideChoice | undefined): string { return this.translate.instant('permissions.' + (c ?? 'inherit')); }
  permChoiceSeverity(c: OverrideChoice | undefined): 'success' | 'danger' | 'secondary' {
    return c === 'grant' ? 'success' : c === 'revoke' ? 'danger' : 'secondary';
  }
  startPermEdit():  void { this.permInitial.set({ ...this.permChoices() }); this.permEditMode.set(true); }
  cancelPermEdit(): void { this.permChoices.set({ ...this.permInitial() }); this.permEditMode.set(false); }

  ngOnInit(): void {
    this.staffId = this.route.snapshot.paramMap.get('id') ?? '';
    this.staffStore.loadRoles();
    this.loadStaff();
    this.loadWorkLogs(1);
    this.loadPermissions();
  }

  loadStaff(): void {
    this.loading.set(true);
    this.staffSvc.getById(this.staffId).subscribe({
      next:  d  => { this.staff.set(d); this.loading.set(false); },
      error: () => { this.toast.error(this.translate.instant('staffDetail.loadFailed')); this.loading.set(false); },
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
  private readonly emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
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
      case 'staffId':   return f.staffId?.trim()   ? '' : this.translate.instant('staff.errStaffIdRequired');
      case 'firstName': return f.firstName?.trim() ? '' : this.translate.instant('staff.errFirstNameRequired');
      case 'email':     return this.emailIsValid() ? '' : this.translate.instant('staff.errEmailInvalid');
    }
  }

  cancelEdit(): void {
    if (this.hasUnsavedChanges()) {
      this.confirmSvc.confirm({
        title: this.translate.instant('staffDetail.discardTitle'),
        message: this.translate.instant('staffDetail.discardMessage'),
        confirmLabel: this.translate.instant('staffDetail.discardConfirm'),
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
      next:  ()       => { this.loadStaff(); this.editMode.set(false); this.saving.set(false); this.toast.success(this.translate.instant('staffDetail.profileUpdated')); },
      error: (e: unknown) => { this.toast.error(apiErrorMessage(e) ?? this.translate.instant('staffDetail.updateFailed')); this.saving.set(false); },
    });
  }

  // ── Permission overrides ────────────────────────────────────────────────────

  /** Loads the catalog of permission keys, then seeds each tri-state from stored overrides. */
  loadPermissions(): void {
    this.permLoading.set(true);
    this.permSvc.catalog().subscribe({
      next: keys => {
        this.permKeys.set(keys);
        this.permSvc.getUserOverrides(this.staffId).subscribe({
          next: overrides => {
            const choices: Record<string, OverrideChoice> = {};
            for (const k of keys) choices[k] = 'inherit';
            for (const o of overrides) choices[o.permissionKey] = o.granted ? 'grant' : 'revoke';
            this.permChoices.set(choices);
            this.permInitial.set({ ...choices });
            this.permLoading.set(false);
          },
          error: () => { this.toast.error(this.translate.instant('permissions.loadFailed')); this.permLoading.set(false); },
        });
      },
      error: () => { this.toast.error(this.translate.instant('permissions.loadFailed')); this.permLoading.set(false); },
    });
  }

  /** Updates a single key's tri-state choice. Coerces null (p-selectButton deselect) back to 'inherit'. */
  setPermChoice(key: string, value: OverrideChoice | null): void {
    this.permChoices.update(c => ({ ...c, [key]: value ?? 'inherit' }));
  }

  /** Persists only the non-inherit (grant/revoke) choices as StaffOverride[]. */
  savePermissions(): void {
    this.permSaving.set(true);
    const choices = this.permChoices();
    const overrides: StaffOverride[] = this.permKeys()
      .filter(k => choices[k] && choices[k] !== 'inherit')
      .map(k => ({ permissionKey: k, granted: choices[k] === 'grant' }));
    this.permSvc.setUserOverrides(this.staffId, overrides).subscribe({
      next:  () => {
        this.toast.success(this.translate.instant('permissions.saved'));
        this.permInitial.set({ ...choices });   // new clean baseline
        this.permEditMode.set(false);            // back to view mode
        this.permSaving.set(false);
      },
      error: () => { this.toast.error(this.translate.instant('permissions.saveFailed')); this.permSaving.set(false); },
    });
  }
}
