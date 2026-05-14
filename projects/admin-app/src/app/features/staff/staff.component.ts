import { Component, OnInit, signal, computed, inject, HostListener, ElementRef, ViewChild } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, StaffStore, RegisterStaffRequest,
         Staff, ToastService, ConfirmService, TooltipDirective, PickerOnlyDirective } from 'shared-lib';

@Component({
  selector: 'app-staff',
  standalone: true,
  imports: [TitleCasePipe, FormsModule, RouterLink, TranslatePipe, TooltipDirective, PickerOnlyDirective],
  templateUrl: './staff.component.html',
  styleUrl: './staff.component.scss',
})
export class StaffComponent implements OnInit {
  protected readonly auth      = inject(AuthService);
  private readonly staffStore  = inject(StaffStore);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly el          = inject(ElementRef);

  @ViewChild('formAnchor') private formAnchor?: ElementRef<HTMLElement>;

  readonly staff   = this.staffStore.staff;
  readonly roles   = this.staffStore.roles;
  readonly loading = this.staffStore.loading;

  search           = signal('');
  readonly pageSize = 10;
  currentPage      = signal(1);
  showForm         = signal(false);
  saving           = signal(false);
  error            = signal('');
  roleDropdownOpen = signal(false);
  openMenuId  = signal<string | null>(null);
  menuPos     = signal<{ top: number; right: number } | null>(null);
  menuTarget  = computed(() => this.staff().find(s => s.id === this.openMenuId()) ?? null);

  @HostListener('document:click', ['$event'])
  onDocumentClick(e: MouseEvent): void {
    if (!this.el.nativeElement.querySelector('.role-multiselect')?.contains(e.target)) {
      this.roleDropdownOpen.set(false);
    }
  }

  newStaff: RegisterStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: '', password: 'password123', roles: [],
    phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
  };

  // Reset password modal state
  resetPassTarget = signal<{ id: string; name: string } | null>(null);
  newPassword     = '';
  showNewPassword = false;
  savingReset     = signal(false);

  filtered = computed(() => {
    const q = this.search().toLowerCase();
    return q
      ? this.staff().filter(s =>
          s.fullName.toLowerCase().includes(q) ||
          s.firstName.toLowerCase().includes(q) ||
          s.lastName.toLowerCase().includes(q) ||
          s.roles.some(r => r.toLowerCase().includes(q)) ||
          s.staffId.toLowerCase().includes(q))
      : this.staff();
  });

  totalPages   = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize)));
  pagedFiltered = computed(() => {
    const page = Math.min(this.currentPage(), this.totalPages());
    return this.filtered().slice((page - 1) * this.pageSize, page * this.pageSize);
  });

  availableRoles = computed(() => {
    const all = this.roles().filter(r => r.name !== 'admin');
    return this.auth.hasRole('admin') ? all : all.filter(r => !r.isSystem);
  });

  ngOnInit(): void {
    this.staffStore.loadStaff();
    this.staffStore.loadRoles();
  }

  toggleMenu(id: string, event: MouseEvent): void {
    if (this.openMenuId() === id) { this.closeMenu(); return; }
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    // right = distance from viewport right to button's right edge
    // → dropdown right edge lines up exactly with the ⋯ button right edge
    const right = window.innerWidth - rect.right;
    this.menuPos.set({ top: rect.bottom + 4, right });
    this.openMenuId.set(id);
  }

  closeMenu(): void { this.openMenuId.set(null); this.menuPos.set(null); }

  openAdd(): void {
    this.showForm.set(true);
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  closeForm(): void {
    this.showForm.set(false);
    this.roleDropdownOpen.set(false);
    this.error.set('');
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
    if (!this.newStaff.staffId || !this.newStaff.firstName || !this.newStaff.lastName || !this.newStaff.email) return;
    const emailRegex = /^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$/;
    if (!emailRegex.test(this.newStaff.email)) {
      this.error.set('Please enter a valid email address.');
      this.toast.error('Please enter a valid email address.');
      return;
    }
    if (!this.newStaff.roles.length) this.newStaff.roles = [this.availableRoles()[0]?.name ?? ''];
    this.saving.set(true);
    this.error.set('');
    this.staffStore.register(this.newStaff).subscribe({
      next: () => {
        this.newStaff = { staffId: '', firstName: '', lastName: '', email: '', password: 'password123',
          roles: [], phoneNumber: '',
          icNo: null, passportNo: null, dateOfBirth: null };
        this.saving.set(false);
        this.showForm.set(false);
        this.roleDropdownOpen.set(false);
        this.toast.success('Staff registered.');
      },
      error: (e) => {
        const msg = e?.error?.error ?? 'Failed to register staff.';
        this.error.set(msg);
        this.toast.error(msg);
        this.saving.set(false);
      },
    });
  }

  toggleRole(roleName: string): void {
    const idx = this.newStaff.roles.indexOf(roleName);
    if (idx === -1) this.newStaff.roles = [...this.newStaff.roles, roleName];
    else            this.newStaff.roles = this.newStaff.roles.filter(r => r !== roleName);
  }

  hasRole(roleName: string): boolean {
    return this.newStaff.roles.includes(roleName);
  }

  toggleActive(id: string, isActive: boolean): void {
    const action = isActive ? 'deactivate' : 'activate';
    this.confirmSvc.confirm({
      title: isActive ? 'Deactivate Staff' : 'Activate Staff',
      message: `Are you sure you want to ${action} this staff member?`,
      confirmLabel: isActive ? 'Deactivate' : 'Activate',
      danger: isActive,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.staffStore.toggleActive(id, isActive).subscribe({
        error: () => this.toast.error('Failed to update status.'),
      });
    });
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
        next: () => this.toast.success(`${s.fullName} deleted.`),
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
