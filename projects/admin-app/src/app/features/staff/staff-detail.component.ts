import { Component, OnInit, signal, computed, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { StaffDetail, UpdateStaffRequest, StaffStore, LocationStore, StaffManagementService, ToastService, ConfirmService, WorkLogService, WorkLogDto, UpdateWorkLogRequest, AmPmPipe, TrimInputDirective, CanComponentDeactivate, apiErrorMessage, PermissionAdminService, StaffOverride, groupedPermissions, LangService, localizedNameFor } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { Dialog } from 'primeng/dialog';
import { Message } from 'primeng/message';
import { TooltipModule } from 'primeng/tooltip';
import { Paginator } from 'primeng/paginator';
import { SelectButton } from 'primeng/selectbutton';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { PayslipDialogComponent } from '../../shared/payslip-dialog/payslip-dialog.component';

/** Tri-state choice for a per-user permission override. */
type OverrideChoice = 'inherit' | 'grant' | 'revoke';
/** Which detail tab is active. */
type DetailTab = 'profile' | 'worklogs' | 'permissions';

@Component({
  selector: 'app-staff-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, TranslatePipe, AmPmPipe, TrimInputDirective,
    Button, InputText, TableModule, Tag, Card, Skeleton, DatePicker, MultiSelect, Select, Dialog, Message, TooltipModule, Paginator, SelectButton, Tabs, TabList, Tab, PayslipDialogComponent],
  templateUrl: './staff-detail.component.html',
  styleUrl: './staff-detail.component.scss',
})
export class StaffDetailComponent implements OnInit, CanComponentDeactivate {
  readonly staffStore  = inject(StaffStore);
  private readonly locationStore = inject(LocationStore);
  private readonly staffSvc    = inject(StaffManagementService);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly route       = inject(ActivatedRoute);
  private readonly workLogSvc  = inject(WorkLogService);
  private readonly permSvc     = inject(PermissionAdminService);
  private readonly translate   = inject(TranslateService);
  private readonly lang        = inject(LangService);

  private readonly payslipDialog = viewChild.required(PayslipDialogComponent);

  /** Open the detailed payslip for this staff member (defaults to the current month). */
  openPayslip(): void {
    const s = this.staff();
    if (s) this.payslipDialog().open(s.staffId, s.fullName);
  }

  staff            = signal<StaffDetail | null>(null);
  loading          = signal(true);
  editMode         = signal(false);
  saving           = signal(false);
  activeTab        = signal<DetailTab>('profile');

  /** Managers now have the same power as admins, so role editing is never hidden. */
  readonly isManager = computed(() => false);

  // Every defined role is assignable (incl. SUPERADMIN); managers and admins have equal power.
  availableRoles = computed(() => this.staffStore.roles());
  availableLocations = computed(() => this.locationStore.locations());

  /** Pick a record's display name in the current UI language (Chinese/Vietnamese when set). */
  private localized(name: string, nameZh: string | null | undefined, nameVi: string | null | undefined): string {
    return localizedNameFor(this.lang.current(), name, nameZh, nameVi);
  }
  /** Resolve a location ID to its (localized) display name — read-only assigned-location tags. */
  locationName(id: string): string {
    const loc = this.availableLocations().find(l => l.id === id);
    return loc ? this.localized(loc.name, loc.nameZh, loc.nameVi) : id;
  }
  /** Resolve a role name to its (localized) display name — read-only assigned-role tags. */
  roleName(name: string): string {
    const role = this.availableRoles().find(r => r.name === name);
    return role ? this.localized(role.name, role.nameZh, role.nameVi) : name;
  }
  /** Localized job name for a shift row — uses the names the backend resolved from the frozen snapshot
   *  (falls back to the snapshot when the job was since renamed/deleted). */
  shiftRole(wl: WorkLogDto): string {
    return this.localized(wl.roleName, wl.roleNameZh, wl.roleNameVi);
  }

  /** Localized location name for a shift row — mirrors shiftRole (falls back to the snapshot when the
   *  location was since deleted). */
  shiftLocation(wl: WorkLogDto): string {
    return this.localized(wl.locationName, wl.locationNameZh, wl.locationNameVi);
  }

  /** Calendar days the shift ran past its work date (0 = same day) — drives the "+N" badge so an
   *  overnight/multi-day shift's end time isn't misread as ending before it started. */
  endDayOffset(wl: WorkLogDto): number {
    if (!wl.endDate || wl.endDate === wl.workDate) return 0;
    const ms = new Date(`${wl.endDate}T00:00:00`).getTime() - new Date(`${wl.workDate}T00:00:00`).getTime();
    return Math.max(0, Math.round(ms / 86_400_000));
  }

  workLogs        = signal<WorkLogDto[]>([]);
  workLogsLoading = signal(false);
  totalLogs       = signal(0);
  currentPage     = signal(1);
  readonly pageSize = 10;
  totalPages = computed(() => Math.max(1, Math.ceil(this.totalLogs() / this.pageSize)));

  // ── Work-log edit (admin can correct a shift directly from this page) ────────
  // Role picker value is the GUID id (edit-staff submits role IDS; work-log update submits roleId).
  readonly roleOptions     = computed(() => this.availableRoles().map(r => ({ label: this.localized(r.name, r.nameZh, r.nameVi), value: r.id })));
  readonly locationOptions = computed(() => this.availableLocations().map(l => ({ label: this.localized(l.name, l.nameZh, l.nameVi), value: l.id })));

  /** Map a role NAME (as stored on the staff record / a work-log snapshot) to its GUID id via the loaded roles. */
  private roleIdForName(name: string): string {
    return this.availableRoles().find(r => r.name === name)?.id ?? '';
  }

  editLogVisible = signal(false);
  editLogSaving  = signal(false);
  editLogError   = signal('');
  editingLog     = signal<WorkLogDto | null>(null);
  editLogForm: UpdateWorkLogRequest = { roleId: '', locationId: '', workDate: '', startTime: '', endDate: '', endTime: '', notes: '' };
  editLogStartTimeDate: Date | null = null;
  editLogEndDate:       Date | null = null;
  editLogEndTimeDate:   Date | null = null;

  /** Minute step for the edit-log END picker — follows the SELECTED job's billing granularity
   *  (FIXED packages step 10 min, HOURLY jobs snap to the 30-min block). The START picker stays a
   *  fixed 30-min step by requirement — only the check-out end follows pay type. Unknown → 30. */
  get editLogEndStep(): number {
    const role = this.availableRoles().find(r => r.id === this.editLogForm.roleId);
    return role?.payType === 'fixed' ? 10 : 30;
  }

  editForm: UpdateStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: null, roles: [], locations: [],
    phoneNumber: null, icNo: null, passportNo: null, dateOfBirth: null,
  };
  private _initialEditForm: UpdateStaffRequest = { ...this.editForm, roles: [], locations: [] };

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
  /** Catalog keys organised into labelled sections for the edit list. */
  readonly visiblePermGroups = computed(() => groupedPermissions(this.permKeys()));

  /** Keys with a non-inherit choice — what the read-only view lists. */
  readonly activeOverrides = computed(() =>
    this.permKeys().filter(k => (this.permChoices()[k] ?? 'inherit') !== 'inherit'));

  // Ticks on language (re)load so instant()-based label computeds re-evaluate — otherwise the
  // SelectButton option labels would cache in the first language and never switch.
  private readonly _langTick = toSignal(
    merge(this.translate.onLangChange, this.translate.onDefaultLangChange), { initialValue: null });

  /** Options for each tri-state p-selectButton (labels resolved at runtime via translate). */
  readonly permOptions = computed<{ label: string; value: OverrideChoice }[]>(() => {
    this._langTick();   // dependency: recompute when the language changes
    return [
      { label: this.translate.instant('permissions.inherit'), value: 'inherit' },
      { label: this.translate.instant('permissions.grant'),   value: 'grant'   },
      { label: this.translate.instant('permissions.revoke'),  value: 'revoke'  },
    ];
  });

  permChoiceLabel(c: OverrideChoice | undefined): string { return this.translate.instant('permissions.' + (c ?? 'inherit')); }
  permChoiceSeverity(c: OverrideChoice | undefined): 'success' | 'danger' | 'secondary' {
    return c === 'grant' ? 'success' : c === 'revoke' ? 'danger' : 'secondary';
  }
  startPermEdit():  void { this.permInitial.set({ ...this.permChoices() }); this.permEditMode.set(true); }
  cancelPermEdit(): void { this.permChoices.set({ ...this.permInitial() }); this.permEditMode.set(false); }

  ngOnInit(): void {
    this.staffId = this.route.snapshot.paramMap.get('id') ?? '';
    this.staffStore.loadRoles();
    this.locationStore.load();
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

  private toLogTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }
  private toLogDateStr(d: Date): string {
    return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
  }
  private parseLogTime(t: string): Date | null {
    if (!t) return null;
    const [h = NaN, m = NaN] = t.split(':').map(Number);
    if (isNaN(h) || isNaN(m)) return null;
    const d = new Date(); d.setHours(h, m, 0, 0); return d;
  }

  openEditLog(wl: WorkLogDto): void {
    this.editLogForm = {
      // WorkLogDto carries only the role NAME snapshot — resolve it to the role id the write DTO needs.
      roleId: this.roleIdForName(wl.roleName), locationId: wl.locationId, workDate: wl.workDate,
      startTime: wl.startTime, endDate: wl.endDate ?? wl.workDate, endTime: wl.endTime ?? '',
      notes: wl.notes ?? '',
    };
    this.editLogStartTimeDate = this.parseLogTime(wl.startTime);
    this.editLogEndTimeDate   = this.parseLogTime(wl.endTime ?? '');
    this.editLogEndDate       = new Date(`${wl.endDate ?? wl.workDate}T00:00:00`);
    this.editLogError.set('');
    this.editingLog.set(wl);
    this.editLogVisible.set(true);
  }

  cancelEditLog(): void {
    this.editLogVisible.set(false);
    this.editingLog.set(null);
    this.editLogStartTimeDate = null;
    this.editLogEndDate       = null;
    this.editLogEndTimeDate   = null;
  }

  saveEditLog(): void {
    const wl = this.editingLog();
    if (!wl) return;
    if (!this.editLogForm.roleId || !this.editLogForm.locationId || !this.editLogForm.workDate
        || !this.editLogStartTimeDate || !this.editLogEndDate || !this.editLogEndTimeDate) {
      this.editLogError.set(this.translate.instant('reports.errRequiredFields')); return;
    }
    const startTime = this.toLogTimeStr(this.editLogStartTimeDate);
    const endTime   = this.toLogTimeStr(this.editLogEndTimeDate);
    const endDate   = this.toLogDateStr(this.editLogEndDate);
    const durationMins = (new Date(`${endDate}T${endTime}:00`).getTime()
                        - new Date(`${this.editLogForm.workDate}T${startTime}:00`).getTime()) / 60000;
    if (durationMins <= 0)        { this.editLogError.set(this.translate.instant('reports.errSameTime')); return; }
    if (durationMins > 14 * 1440) { this.editLogError.set(this.translate.instant('reports.errTooLong')); return; }

    this.editLogSaving.set(true);
    this.editLogError.set('');
    this.workLogSvc.update(wl.id, { ...this.editLogForm, startTime, endDate, endTime, notes: this.editLogForm.notes || null }).subscribe({
      next: () => {
        this.editLogSaving.set(false);
        this.cancelEditLog();
        this.toast.success(this.translate.instant('reports.shiftUpdated'));
        this.loadWorkLogs(this.currentPage());
      },
      error: (e: unknown) => {
        this.editLogError.set(apiErrorMessage(e) ?? this.translate.instant('reports.shiftUpdateFailed'));
        this.editLogSaving.set(false);
      },
    });
  }

  deleteLog(wl: WorkLogDto): void {
    this.confirmSvc.confirm({
      message: this.translate.instant('staffDetail.deleteWorkLogConfirm'),
      danger: true,
    }).subscribe(ok => {
      if (!ok) return;
      this.workLogSvc.delete(wl.id).subscribe({
        next: () => {
          this.toast.success(this.translate.instant('staffDetail.workLogDeleted'));
          this.loadWorkLogs(this.currentPage());
        },
        error: (e: unknown) => this.toast.error(apiErrorMessage(e) ?? this.translate.instant('staffDetail.workLogDeleteFailed')),
      });
    });
  }

  startEdit(): void {
    const s = this.staff()!;
    this.editForm = {
      staffId: s.staffId, firstName: s.firstName, lastName: s.lastName,
      email: s.email || null,
      // s.roles are NAMES (display) but the update DTO + role picker use IDS — map name → id.
      roles: s.roles.map(name => this.roleIdForName(name)).filter(id => id),
      locations: [...(s.locationIds ?? [])], phoneNumber: s.phoneNumber,
      icNo: s.icNo ?? null, passportNo: s.passportNo ?? null,
      dateOfBirth: s.dateOfBirth ?? null,
    };
    this._initialEditForm = { ...this.editForm, roles: [...this.editForm.roles], locations: [...this.editForm.locations] };
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
