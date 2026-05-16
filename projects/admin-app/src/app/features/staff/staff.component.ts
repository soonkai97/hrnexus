import { Component, OnInit, signal, computed, inject, ElementRef, ViewChild } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService, StaffStore, RegisterStaffRequest,
         Staff, ToastService, ConfirmService } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
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

@Component({
  selector: 'app-staff',
  standalone: true,
  imports: [TitleCasePipe, FormsModule, RouterLink, TranslatePipe,
    Button, InputText, Password, TableModule, Tag, Card, Dialog, TooltipModule, Message, DatePicker,
    MultiSelect, Menu],
  templateUrl: './staff.component.html',
  styleUrl: './staff.component.scss',
})
export class StaffComponent implements OnInit {
  protected readonly auth      = inject(AuthService);
  private readonly staffStore  = inject(StaffStore);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly translate   = inject(TranslateService);

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;
  @ViewChild('staffMenu') staffMenu!: Menu;

  readonly staff      = this.staffStore.staff;
  readonly roles      = this.staffStore.roles;
  readonly loading    = this.staffStore.loading;
  readonly totalCount = this.staffStore.totalCount;

  /** Used as [maxDate] on the date-of-birth picker to prevent future dates. */
  readonly todayDate = new Date();

  search           = signal('');
  readonly pageSize = 10;
  currentPage      = signal(1);
  totalPages       = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));
  showForm         = signal(false);
  saving           = signal(false);
  error            = signal('');
  staffMenuItems   = signal<MenuItem[]>([]);

  newStaff: RegisterStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: '', password: 'password123', roles: [],
    phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: '',
  };

  // Reset password modal state
  resetPassTarget = signal<{ id: string; name: string } | null>(null);
  newPassword     = '';
  showNewPassword = false;
  savingReset     = signal(false);

  /** Mirrors the required-field checks in saveStaff() so Save can be disabled upfront. */
  get isFormValid(): boolean {
    return !!this.newStaff.staffId?.trim() &&
      !!this.newStaff.firstName?.trim() &&
      !!this.newStaff.lastName?.trim() &&
      !!this.newStaff.email?.trim() &&
      !!(this.newStaff.icNo?.trim() || this.newStaff.passportNo?.trim()) &&
      !!this.newStaff.dateOfBirth;
  }

  availableRoles = computed(() => {
    const all = this.roles().filter(r => r.name !== 'admin');
    return this.auth.hasRole('admin') ? all : all.filter(r => !r.isSystem);
  });

  ngOnInit(): void {
    this.staffStore.loadStaff('', 1, this.pageSize);
    this.staffStore.loadRoles();
  }

  loadPage(page: number): void {
    this.currentPage.set(page);
    this.staffStore.loadStaff(this.search(), page, this.pageSize);
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.currentPage.set(1);
    this.staffStore.loadStaff(value, 1, this.pageSize);
  }

  openStaffMenu(s: Staff, event: Event): void {
    this.staffMenuItems.set([
      {
        label: this.translate.instant('staff.editStaff'),
        routerLink: ['/staff', s.id],
      },
      {
        label: this.translate.instant('staff.resetPassword'),
        command: () => this.openResetPassword(s),
      },
      { separator: true },
      {
        label: s.isActive
          ? this.translate.instant('staff.deactivate')
          : this.translate.instant('staff.activate'),
        command: () => this.toggleActive(s.id, s.isActive),
      },
      {
        label: this.translate.instant('staff.delete'),
        styleClass: 'nc-danger-item',
        command: () => this.deleteStaff(s),
      },
    ]);
    this.staffMenu.toggle(event);
  }

  openAdd(): void {
    this.showForm.set(true);
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  closeForm(): void {
    this.showForm.set(false);
    this.error.set('');
    this.newStaff = { staffId: '', firstName: '', lastName: '', email: '', password: 'password123',
      roles: [], phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: '' };
  }

  /** Mask a phone number: +601X•••••89 */
  maskPhone(phone: string | null | undefined): string {
    if (!phone) return '—';
    const digits = phone.replace(/\D/g, '');
    if (digits.length < 4) return phone;
    const prefix = phone.startsWith('+') ? '+' : '';
    const visible = digits.slice(0, 4);
    const last2   = digits.slice(-2);
    const masked  = '•'.repeat(Math.max(0, digits.length - 6));
    return `${prefix}${visible}${masked}${last2}`;
  }

  saveStaff(): void {
    if (!this.newStaff.staffId || !this.newStaff.firstName || !this.newStaff.lastName || !this.newStaff.email) {
      this.error.set('Please fill in Staff ID, First Name, Last Name and Email.');
      return;
    }
    const emailRegex = /^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$/;
    if (!emailRegex.test(this.newStaff.email)) {
      this.error.set('Please enter a valid email address.');
      this.toast.error('Please enter a valid email address.');
      return;
    }
    if (!this.newStaff.icNo && !this.newStaff.passportNo) {
      this.error.set('Please enter an IC number or Passport number.');
      return;
    }
    if (!this.newStaff.dateOfBirth) {
      this.error.set('Date of birth is required.');
      return;
    }
    if (!this.newStaff.roles.length) {
      const defaultRole = this.availableRoles()[0]?.name;
      if (!defaultRole) { this.error.set('Please select at least one role.'); return; }
      this.newStaff.roles = [defaultRole];
    }
    this.saving.set(true);
    this.error.set('');
    this.staffStore.register(this.newStaff).subscribe({
      next: () => {
        this.newStaff = { staffId: '', firstName: '', lastName: '', email: '', password: 'password123',
          roles: [], phoneNumber: '',
          icNo: null, passportNo: null, dateOfBirth: '' };
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
    this.newPassword     = '';
    this.showNewPassword = false;
  }

  closeResetPassword(): void { this.resetPassTarget.set(null); }

  generatePassword(): void {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$';
    this.newPassword = Array.from({ length: 10 }, () =>
      chars[Math.floor(Math.random() * chars.length)]).join('');
    this.showNewPassword = true;
  }

  saveResetPassword(): void {
    const target = this.resetPassTarget();
    if (!target || !this.newPassword || this.newPassword.length < 8) return;
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
}
