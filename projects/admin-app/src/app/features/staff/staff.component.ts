import { Component, OnInit, OnDestroy, signal, computed, inject, ViewChild } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { StaffStore, LocationStore, RegisterStaffRequest,
         Staff, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate,
         passwordChecks, isStrongPassword, BulkStaffImportResult, BulkStaffImportRow } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Paginator } from 'primeng/paginator';
import { Password } from 'primeng/password';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { TooltipModule } from 'primeng/tooltip';
import { Message } from 'primeng/message';
import { DatePicker } from 'primeng/datepicker';
import { MultiSelect } from 'primeng/multiselect';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { Skeleton } from 'primeng/skeleton';
import { Checkbox } from 'primeng/checkbox';

@Component({
  selector: 'app-staff',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, TrimInputDirective,
    Button, InputText, IconField, InputIcon, Password, TableModule, Tag, Card, Dialog, TooltipModule, Message, DatePicker,
    MultiSelect, Menu, Paginator, Skeleton, Checkbox],
  templateUrl: './staff.component.html',
  styleUrl: './staff.component.scss',
})
export class StaffComponent implements OnInit, OnDestroy, CanComponentDeactivate {
  private readonly staffStore  = inject(StaffStore);
  private readonly locationStore = inject(LocationStore);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly translate   = inject(TranslateService);

  @ViewChild('staffMenu') staffMenu!: Menu;

  readonly staff      = this.staffStore.staff;
  readonly roles      = this.staffStore.roles;
  readonly loading    = this.staffStore.loading;
  readonly totalCount = this.staffStore.totalCount;

  /** Used as [maxDate] on the date-of-birth picker to prevent future dates. */
  readonly todayDate = new Date();

  search           = signal('');
  private readonly searchSubject = new Subject<string>();
  private searchSub?: Subscription;
  readonly pageSize = 10;
  currentPage      = signal(1);
  totalPages       = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));
  showForm         = signal(false);
  saving           = signal(false);
  error            = signal('');
  staffMenuItems   = signal<MenuItem[]>([]);

  newStaff: RegisterStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: null, password: '', roles: [], locations: [],
    phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
  };

  showNewStaffPwd = signal(false);

  // Reset password modal state
  resetPassTarget = signal<{ id: string; name: string } | null>(null);
  newPassword     = '';
  savingReset     = signal(false);

  /** Gates the Save button — Staff ID, first name, and a policy-compliant initial password. */
  get isFormValid(): boolean {
    return !!this.newStaff.staffId?.trim() &&
      !!this.newStaff.firstName?.trim() &&
      isStrongPassword(this.newStaff.password);
  }

  /** Live password-requirement state for the checklist UI. */
  get pwdChecks() { return passwordChecks(this.newStaff.password); }

  // ── Per-field inline validation ───────────────────────────────────────────────
  private readonly emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
  readonly touched = signal<Set<string>>(new Set());

  /** Mark a field as blurred so its inline error can show (don't nag before first interaction). */
  markTouched(field: string): void {
    this.touched.update(s => new Set(s).add(field));
  }

  /** Inline error text for a field — empty string when valid or not yet touched. */
  fieldError(field: 'staffId' | 'firstName' | 'email' | 'password'): string {
    if (!this.touched().has(field)) return '';
    const f = this.newStaff;
    switch (field) {
      case 'staffId':   return f.staffId?.trim()   ? '' : this.translate.instant('staff.errStaffIdRequired');
      case 'firstName': return f.firstName?.trim() ? '' : this.translate.instant('staff.errFirstNameRequired');
      case 'password':  return isStrongPassword(f.password) ? '' : this.translate.instant('staff.errPasswordWeak');
      case 'email': {
        const e = f.email?.trim();
        return !e || this.emailRegex.test(e) ? '' : this.translate.instant('staff.errEmailInvalid');
      }
    }
  }

  generateNewStaffPassword(): void {
    const upper  = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
    const lower  = 'abcdefghjkmnpqrstuvwxyz';
    const digits = '23456789';
    const all    = upper + lower + digits + '!@#$';
    const pick   = (set: string) => set[Math.floor(Math.random() * set.length)];
    // Guarantee at least one uppercase and one digit (policy), fill the rest, then shuffle
    // so the guaranteed characters aren't always in a fixed position.
    const chars = [pick(upper), pick(digits), ...Array.from({ length: 8 }, () => pick(all))];
    for (let i = chars.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [chars[i], chars[j]] = [chars[j], chars[i]];
    }
    this.newStaff.password = chars.join('');
    this.showNewStaffPwd.set(true);
  }

  // Every defined role is assignable (incl. SUPERADMIN); managers and admins have equal power.
  availableRoles = computed(() => this.roles());
  availableLocations = computed(() => this.locationStore.locations());

  ngOnInit(): void {
    this.staffStore.loadStaff('', 1, this.pageSize);
    this.staffStore.loadRoles();
    this.locationStore.load();
    this.searchSub = this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
    ).subscribe(value => {
      this.staffStore.loadStaff(value, 1, this.pageSize);
    });
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
  }

  loadPage(page: number): void {
    this.currentPage.set(page);
    this.staffStore.loadStaff(this.search(), page, this.pageSize);
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.currentPage.set(1);
    this.searchSubject.next(value);
  }

  openStaffMenu(s: Staff, event: Event): void {
    this.staffMenuItems.set([
      {
        label: this.translate.instant('staff.editStaff'),
        icon: 'pi pi-pencil',
        routerLink: ['/staff', s.id],
      },
      {
        label: this.translate.instant('staff.resetPassword'),
        icon: 'pi pi-key',
        command: () => this.openResetPassword(s),
      },
      { separator: true },
      {
        label: s.isActive
          ? this.translate.instant('staff.deactivate')
          : this.translate.instant('staff.activate'),
        icon: 'pi pi-power-off',
        command: () => this.toggleActive(s.id, s.isActive),
      },
      {
        label: this.translate.instant('staff.delete'),
        icon: 'pi pi-trash',
        styleClass: 'nc-danger-item',
        command: () => this.deleteStaff(s),
      },
    ]);
    this.staffMenu.toggle(event);
  }

  openAdd(): void {
    this.error.set('');
    this.touched.set(new Set());
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
    this.error.set('');
    this.touched.set(new Set());
    this.showNewStaffPwd.set(false);
    this.newStaff = { staffId: '', firstName: '', lastName: '', email: null, password: '',
      roles: [], locations: [], phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null };
  }

  saveStaff(): void {
    // Email is optional but must be valid if provided
    const email = this.newStaff.email?.trim() || null;
    if (email) {
      const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
      if (!emailRegex.test(email)) {
        this.error.set(this.translate.instant('staff.errEmailInvalidFull'));
        this.toast.error(this.translate.instant('staff.errEmailInvalidFull'));
        return;
      }
    }

    if (!this.newStaff.roles.length) {
      const defaultRole = this.availableRoles()[0]?.name;
      if (!defaultRole) { this.error.set(this.translate.instant('staff.errSelectRole')); return; }
      this.newStaff.roles = [defaultRole];
    }
    this.saving.set(true);
    this.error.set('');
    const req = { ...this.newStaff, email };   // normalise empty string → null
    this.staffStore.register(req).subscribe({
      next: () => {
        this.newStaff = { staffId: '', firstName: '', lastName: '', email: null, password: '',
          roles: [], locations: [], phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null };
        this.showNewStaffPwd.set(false);
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success(this.translate.instant('staff.registered'));
        this.search.set('');
        this.loadPage(1);
      },
      error: (e) => {
        const msg = e?.error?.error ?? this.translate.instant('staff.registerFailed');
        this.error.set(msg);
        this.toast.error(msg);
        this.saving.set(false);
      },
    });
  }

  toggleActive(id: string, isActive: boolean): void {
    if (isActive) {
      const fullName = this.staff().find(s => s.id === id)?.fullName ?? this.translate.instant('staff.thisStaffMember');
      this.confirmSvc.confirm({
        title: this.translate.instant('staff.deactivateTitle'),
        message: this.translate.instant('staff.deactivateMessage', { name: fullName }),
        confirmLabel: this.translate.instant('staff.deactivate'),
        danger: true,
      }).subscribe((confirmed: boolean) => {
        if (!confirmed) return;
        this.staffStore.toggleActive(id, isActive).subscribe({
          error: () => this.toast.error(this.translate.instant('staff.statusUpdateFailed')),
        });
      });
    } else {
      this.staffStore.toggleActive(id, isActive).subscribe({
        error: () => this.toast.error(this.translate.instant('staff.statusUpdateFailed')),
      });
    }
  }

  // ── Delete Staff ────────────────────────────────────────────────────────────

  deleteStaff(s: Staff): void {
    this.confirmSvc.confirm({
      title:        this.translate.instant('staff.deleteTitle'),
      message:      this.translate.instant('staff.deleteMessage', { name: s.fullName }),
      confirmLabel: this.translate.instant('common.delete'),
      danger:       true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.staffStore.deleteStaff(s.id).subscribe({
        next: () => {
          this.toast.success(this.translate.instant('staff.deletedToast', { name: s.fullName }));
          this.loadPage(this.currentPage());
        },
        error: (e) => this.toast.error(e?.error?.error ?? this.translate.instant('staff.deleteFailed')),
      });
    });
  }

  // ── Reset Password ──────────────────────────────────────────────────────────

  openResetPassword(s: Staff): void {
    this.resetPassTarget.set({ id: s.id, name: s.fullName ?? `${s.firstName} ${s.lastName}` });
    this.newPassword = '';
  }

  closeResetPassword(): void { this.resetPassTarget.set(null); }

  /** Live password-requirement state for the reset-password modal checklist. */
  get resetPwdChecks() { return passwordChecks(this.newPassword); }
  get isResetPwdValid(): boolean { return isStrongPassword(this.newPassword); }

  generatePassword(): void {
    const upper  = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
    const lower  = 'abcdefghjkmnpqrstuvwxyz';
    const digits = '23456789';
    const all    = upper + lower + digits + '!@#$';
    const pick   = (set: string) => set[Math.floor(Math.random() * set.length)];
    const chars = [pick(upper), pick(digits), ...Array.from({ length: 8 }, () => pick(all))];
    for (let i = chars.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [chars[i], chars[j]] = [chars[j], chars[i]];
    }
    this.newPassword = chars.join('');
  }

  saveResetPassword(): void {
    const target = this.resetPassTarget();
    if (!target || !isStrongPassword(this.newPassword)) return;
    this.savingReset.set(true);
    this.staffStore.resetPassword(target.id, this.newPassword).subscribe({
      next: () => {
        this.savingReset.set(false);
        this.resetPassTarget.set(null);
        this.toast.success(this.translate.instant('staff.passwordResetToast', { name: target.name }));
      },
      error: (e) => {
        const msg = e?.error?.error ?? this.translate.instant('staff.resetFailed');
        this.toast.error(msg);
        this.savingReset.set(false);
      },
    });
  }

  // ── Bulk actions ──────────────────────────────────────────────────────────
  bulkSelecting    = signal(false);
  selectedIds      = signal<Set<string>>(new Set());
  bulkSaving       = signal(false);
  showBulkLocations = signal(false);
  showBulkRoles     = signal(false);
  bulkLocations: string[] = [];
  bulkRoles:     string[] = [];

  selectedCount = computed(() => this.selectedIds().size);
  allVisibleSelected = computed(() => {
    const ids = this.selectedIds();
    const rows = this.staff();
    return rows.length > 0 && rows.every(s => ids.has(s.id));
  });

  toggleBulkMode(): void {
    this.bulkSelecting.update(v => !v);
    if (!this.bulkSelecting()) this.selectedIds.set(new Set());
  }

  toggleSelectAll(): void {
    const rows = this.staff();
    const next = new Set(this.selectedIds());
    if (this.allVisibleSelected()) rows.forEach(s => next.delete(s.id));
    else                           rows.forEach(s => next.add(s.id));
    this.selectedIds.set(next);
  }

  toggleSelect(id: string): void {
    const next = new Set(this.selectedIds());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.selectedIds.set(next);
  }

  openBulkLocations(): void { this.bulkLocations = []; this.showBulkLocations.set(true); }
  openBulkRoles():     void { this.bulkRoles = [];     this.showBulkRoles.set(true); }

  private afterBulk(): void {
    this.selectedIds.set(new Set());
    this.bulkSelecting.set(false);
    this.loadPage(this.currentPage());
  }

  saveBulkLocations(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length || !this.bulkLocations.length) return;
    this.bulkSaving.set(true);
    this.staffStore.bulkAssignLocations(ids, this.bulkLocations).subscribe({
      next: (r) => {
        this.bulkSaving.set(false);
        this.showBulkLocations.set(false);
        this.toast.success(this.translate.instant('staff.bulkLocationsToast', { n: r.count }));
        this.afterBulk();
      },
      error: (e) => {
        this.bulkSaving.set(false);
        this.toast.error(e?.error?.error ?? this.translate.instant('staff.bulkFailed'));
      },
    });
  }

  saveBulkRoles(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length || !this.bulkRoles.length) return;
    this.bulkSaving.set(true);
    this.staffStore.bulkAssignRoles(ids, this.bulkRoles).subscribe({
      next: (r) => {
        this.bulkSaving.set(false);
        this.showBulkRoles.set(false);
        this.toast.success(this.translate.instant('staff.bulkRolesToast', { n: r.count }));
        this.afterBulk();
      },
      error: (e) => {
        this.bulkSaving.set(false);
        this.toast.error(e?.error?.error ?? this.translate.instant('staff.bulkFailed'));
      },
    });
  }

  bulkDeactivateSelected(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length) return;
    this.confirmSvc.confirm({
      title:        this.translate.instant('staff.bulkDeactivateTitle'),
      message:      this.translate.instant('staff.bulkDeactivateMessage', { n: ids.length }),
      confirmLabel: this.translate.instant('staff.deactivate'),
      danger:       true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.bulkSaving.set(true);
      this.staffStore.bulkDeactivate(ids).subscribe({
        next: (r) => {
          this.bulkSaving.set(false);
          this.toast.success(this.translate.instant('staff.bulkDeactivatedToast', { n: r.count }));
          this.afterBulk();
        },
        error: (e) => {
          this.bulkSaving.set(false);
          this.toast.error(e?.error?.error ?? this.translate.instant('staff.bulkFailed'));
        },
      });
    });
  }

  // ── Bulk import (editable row grid) ─────────────────────────────────────────
  showImport   = signal(false);
  importing    = signal(false);
  importResult = signal<BulkStaffImportResult | null>(null);
  /** One editable row per staff to import. Roles is a multiselect; password is auto-generated. */
  importRows: { staffId: string; firstName: string; lastName: string; email: string; roles: string[] }[] = [];

  private blankImportRow() { return { staffId: '', firstName: '', lastName: '', email: '', roles: [] as string[] }; }

  openImport(): void {
    this.importResult.set(null);
    this.importRows = [this.blankImportRow(), this.blankImportRow(), this.blankImportRow()];
    this.showImport.set(true);
  }

  addImportRow(): void { this.importRows = [...this.importRows, this.blankImportRow()]; }
  removeImportRow(i: number): void { this.importRows = this.importRows.filter((_, idx) => idx !== i); }

  /** Rows with at least a Staff ID or first name — fully-blank rows are ignored. */
  get importRowsToSubmit(): BulkStaffImportRow[] {
    return this.importRows
      .map(r => ({
        staffId: r.staffId?.trim() ?? '', firstName: r.firstName?.trim() ?? '',
        lastName: r.lastName?.trim() || null, email: r.email?.trim() || null,
        roles: r.roles ?? [],
      }) as BulkStaffImportRow)
      .filter(r => r.staffId.length > 0 || r.firstName.length > 0 || r.roles.length > 0);
  }

  runImport(): void {
    const rows = this.importRowsToSubmit;
    if (!rows.length) return;
    this.importing.set(true);
    this.staffStore.bulkImportStaff(rows).subscribe({
      next: (res) => {
        this.importing.set(false);
        this.importResult.set(res);
        if (res.created > 0) {
          this.toast.success(this.translate.instant('staff.importedToast', { created: res.created, failed: res.failed }));
          this.loadPage(1);
        } else {
          this.toast.warning(this.translate.instant('staff.importedToast', { created: res.created, failed: res.failed }));
        }
      },
      error: (e) => {
        this.importing.set(false);
        this.toast.error(e?.error?.error ?? this.translate.instant('staff.bulkFailed'));
      },
    });
  }

  /** Copy "staffId  password" for every successfully-imported row to the clipboard. */
  copyCredentials(): void {
    const lines = (this.importResult()?.rows ?? [])
      .filter(r => r.success && r.tempPassword)
      .map(r => `${r.staffId}\t${r.tempPassword}`);
    if (!lines.length) return;
    navigator.clipboard?.writeText(lines.join('\n')).then(
      () => this.toast.success(this.translate.instant('staff.credentialsCopied')),
      () => this.toast.error(this.translate.instant('staff.bulkFailed')),
    );
  }

  hasUnsavedChanges(): boolean {
    return this.showForm() && (
      !!this.newStaff.staffId?.trim()   ||
      !!this.newStaff.firstName?.trim() ||
      !!this.newStaff.lastName?.trim()  ||
      !!this.newStaff.email?.trim()     ||
      !!this.newStaff.password
    );
  }
}
