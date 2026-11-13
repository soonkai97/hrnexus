import { Component, OnInit, OnDestroy, signal, computed, inject, ViewChild } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { StaffStore, RegisterStaffRequest,
         Staff, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate,
         passwordChecks, isStrongPassword } from 'shared-lib';
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
  imports: [FormsModule, RouterLink, TrimInputDirective,
    Button, InputText, IconField, InputIcon, Password, TableModule, Tag, Card, Dialog, Message, DatePicker,
    MultiSelect, Menu, Paginator, Skeleton, Checkbox],
  templateUrl: './staff.component.html',
  styleUrl: './staff.component.scss',
})
export class StaffComponent implements OnInit, OnDestroy, CanComponentDeactivate {
  private readonly staffStore  = inject(StaffStore);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);

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
  /** When on, hidden (e.g. seeded system) accounts are included in the roster so they can be unhidden. */
  showHidden       = signal(false);

  newStaff: RegisterStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: null, password: '', roles: [],
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
      case 'staffId':   return f.staffId?.trim()   ? '' : 'Staff ID is required.';
      case 'firstName': return f.firstName?.trim() ? '' : 'First name is required.';
      case 'password':  return isStrongPassword(f.password) ? '' : 'Min. 6 characters.';
      case 'email': {
        const e = f.email?.trim();
        return !e || this.emailRegex.test(e) ? '' : 'Enter a valid email address.';
      }
    }
  }

  // Every defined role is assignable (incl. SUPERADMIN); managers and admins have equal power.
  availableRoles = computed(() => this.roles());

  // Picker options; the value is the role GUID id (register/update expect role IDS).
  readonly roleOptions = computed(() => this.availableRoles().map(r => ({ label: r.name, value: r.id })));

  ngOnInit(): void {
    this.staffStore.loadStaff('', 1, this.pageSize, this.showHidden());
    this.staffStore.loadRoles();
    this.searchSub = this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
    ).subscribe(value => {
      this.staffStore.loadStaff(value, 1, this.pageSize, this.showHidden());
    });
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
  }

  loadPage(page: number): void {
    this.currentPage.set(page);
    this.staffStore.loadStaff(this.search(), page, this.pageSize, this.showHidden());
  }

  /** Toggle whether hidden accounts appear in the list, then reload from page 1. */
  toggleShowHidden(): void {
    this.showHidden.update(v => !v);
    this.loadPage(1);
  }

  /** Hide or unhide a staff account in the roster (presentational only). */
  setHidden(s: Staff, hidden: boolean): void {
    this.staffStore.setHidden(s.id, hidden).subscribe({
      next: () => {
        this.toast.success(hidden ? `${s.fullName} is now hidden from the list.` : `${s.fullName} is now shown in the list.`);
        this.loadPage(this.currentPage());
      },
      error: (e) => this.toast.error(e?.error?.error ?? 'Failed to update status.'),
    });
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.currentPage.set(1);
    this.searchSubject.next(value);
  }

  openStaffMenu(s: Staff, event: Event): void {
    this.staffMenuItems.set([
      {
        label: 'Edit',
        icon: 'pi pi-pencil',
        routerLink: ['/staff', s.id],
      },
      {
        label: 'Reset Password',
        icon: 'pi pi-key',
        command: () => this.openResetPassword(s),
      },
      { separator: true },
      {
        label: s.isActive
          ? 'Deactivate'
          : 'Activate',
        icon: 'pi pi-power-off',
        command: () => this.toggleActive(s.id, s.isActive),
      },
      {
        label: s.isHidden
          ? 'Unhide'
          : 'Hide from list',
        icon: s.isHidden ? 'pi pi-eye' : 'pi pi-eye-slash',
        command: () => this.setHidden(s, !s.isHidden),
      },
      {
        label: 'Delete',
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
      roles: [], phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null };
  }

  saveStaff(): void {
    // Email is optional but must be valid if provided
    const email = this.newStaff.email?.trim() || null;
    if (email) {
      const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
      if (!emailRegex.test(email)) {
        this.error.set('Please enter a valid email address.');
        this.toast.error('Please enter a valid email address.');
        return;
      }
    }

    if (!this.newStaff.roles.length) {
      const defaultRole = this.availableRoles()[0]?.id;
      if (!defaultRole) { this.error.set('Please select at least one job.'); return; }
      this.newStaff.roles = [defaultRole];
    }
    this.saving.set(true);
    this.error.set('');
    const req = { ...this.newStaff, email };   // normalise empty string → null
    this.staffStore.register(req).subscribe({
      next: () => {
        this.newStaff = { staffId: '', firstName: '', lastName: '', email: null, password: '',
          roles: [], phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null };
        this.showNewStaffPwd.set(false);
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success('Staff registered.');
        this.search.set('');
        this.loadPage(1);
      },
      error: (e) => {
        const msg = e?.error?.error ?? 'Failed to register staff.';
        this.error.set(msg);
        this.toast.error(msg);
        this.saving.set(false);
      },
    });
  }

  toggleActive(id: string, isActive: boolean): void {
    if (isActive) {
      const fullName = this.staff().find(s => s.id === id)?.fullName ?? 'this staff member';
      this.confirmSvc.confirm({
        title: 'Deactivate Staff',
        message: `This will prevent ${fullName} from logging in. Are you sure?`,
        confirmLabel: 'Deactivate',
        danger: true,
      }).subscribe((confirmed: boolean) => {
        if (!confirmed) return;
        this.staffStore.toggleActive(id, isActive).subscribe({
          error: () => this.toast.error('Failed to update status.'),
        });
      });
    } else {
      this.staffStore.toggleActive(id, isActive).subscribe({
        error: () => this.toast.error('Failed to update status.'),
      });
    }
  }

  // ── Delete Staff ────────────────────────────────────────────────────────────

  deleteStaff(s: Staff): void {
    this.confirmSvc.confirm({
      title:        'Delete Staff Member',
      message:      `Permanently delete ${s.fullName}? This cannot be undone. Staff with shift history cannot be deleted — deactivate them instead.`,
      confirmLabel: 'Delete',
      danger:       true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.staffStore.deleteStaff(s.id).subscribe({
        next: () => {
          this.toast.success(`${s.fullName} deleted.`);
          this.loadPage(this.currentPage());
        },
        error: (e) => this.toast.error(e?.error?.error ?? 'Failed to delete staff.'),
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

  saveResetPassword(): void {
    const target = this.resetPassTarget();
    if (!target || !isStrongPassword(this.newPassword)) return;
    this.savingReset.set(true);
    this.staffStore.resetPassword(target.id, this.newPassword).subscribe({
      next: () => {
        this.savingReset.set(false);
        this.resetPassTarget.set(null);
        this.toast.success(`Password reset for ${target.name}. Inform them of the new password verbally.`);
      },
      error: (e) => {
        const msg = e?.error?.error ?? 'Failed to reset password.';
        this.toast.error(msg);
        this.savingReset.set(false);
      },
    });
  }

  // ── Bulk actions ──────────────────────────────────────────────────────────
  bulkSelecting    = signal(false);
  selectedIds      = signal<Set<string>>(new Set());
  bulkSaving       = signal(false);
  showBulkRoles     = signal(false);
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

  openBulkRoles(): void { this.bulkRoles = []; this.showBulkRoles.set(true); }

  private afterBulk(): void {
    this.selectedIds.set(new Set());
    this.bulkSelecting.set(false);
    this.loadPage(this.currentPage());
  }

  saveBulkRoles(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length || !this.bulkRoles.length) return;
    this.bulkSaving.set(true);
    this.staffStore.bulkAssignRoles(ids, this.bulkRoles).subscribe({
      next: (r) => {
        this.bulkSaving.set(false);
        this.showBulkRoles.set(false);
        this.toast.success(`Jobs assigned to ${r.count} staff.`);
        this.afterBulk();
      },
      error: (e) => {
        this.bulkSaving.set(false);
        this.toast.error(e?.error?.error ?? 'Bulk action failed.');
      },
    });
  }

  bulkDeactivateSelected(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length) return;
    this.confirmSvc.confirm({
      title:        'Deactivate staff',
      message:      `Deactivate ${ids.length} selected staff member(s)? They will no longer be able to sign in.`,
      confirmLabel: 'Deactivate',
      danger:       true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.bulkSaving.set(true);
      this.staffStore.bulkDeactivate(ids).subscribe({
        next: (r) => {
          this.bulkSaving.set(false);
          this.toast.success(`${r.count} staff deactivated.`);
          this.afterBulk();
        },
        error: (e) => {
          this.bulkSaving.set(false);
          this.toast.error(e?.error?.error ?? 'Bulk action failed.');
        },
      });
    });
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
