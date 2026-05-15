import { Component, OnInit, signal, computed, inject, HostListener, ElementRef } from '@angular/core';
import { DatePipe, DecimalPipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { StaffDetail, UpdateStaffRequest, StaffStore, StaffService, ToastService, WorkLogService, WorkLogDto, AmPmPipe, PickerOnlyDirective } from 'shared-lib';

@Component({
  selector: 'app-staff-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, TitleCasePipe, FormsModule, RouterLink, TranslatePipe, AmPmPipe, PickerOnlyDirective],
  templateUrl: './staff-detail.component.html',
  styleUrl: './staff-detail.component.scss',
})
export class StaffDetailComponent implements OnInit {
  readonly staffStore  = inject(StaffStore);
  private readonly staffSvc   = inject(StaffService);
  private readonly toast      = inject(ToastService);
  private readonly route      = inject(ActivatedRoute);
  private readonly el         = inject(ElementRef);
  private readonly workLogSvc = inject(WorkLogService);

  staff            = signal<StaffDetail | null>(null);
  loading          = signal(true);
  editMode         = signal(false);
  saving           = signal(false);
  roleDropdownOpen = signal(false);

  workLogs        = signal<WorkLogDto[]>([]);
  workLogsLoading = signal(false);
  totalLogs       = signal(0);
  currentPage     = signal(1);
  readonly pageSize = 10;
  totalPages = computed(() => Math.max(1, Math.ceil(this.totalLogs() / this.pageSize)));

  @HostListener('document:click', ['$event'])
  onDocumentClick(e: MouseEvent): void {
    if (!this.el.nativeElement.querySelector('.role-multiselect')?.contains(e.target)) {
      this.roleDropdownOpen.set(false);
    }
  }

  editForm: UpdateStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: '', roles: [],
    phoneNumber: null, icNo: null, passportNo: null, dateOfBirth: null,
  };

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
      staffId: s.staffId, firstName: s.firstName, lastName: s.lastName, email: s.email,
      roles: [...s.roles], phoneNumber: s.phoneNumber,
      icNo: s.icNo ?? null, passportNo: s.passportNo ?? null, dateOfBirth: s.dateOfBirth ?? null,
    };
    this.editMode.set(true);
  }

  toggleDetailRole(roleName: string): void {
    const idx = this.editForm.roles.indexOf(roleName);
    if (idx === -1) this.editForm = { ...this.editForm, roles: [...this.editForm.roles, roleName] };
    else            this.editForm = { ...this.editForm, roles: this.editForm.roles.filter(r => r !== roleName) };
  }

  /** Gates the Save button — mirrors the backend required-field checks. */
  get isEditFormValid(): boolean {
    return !!this.editForm.staffId?.trim() &&
      !!this.editForm.firstName?.trim() &&
      !!this.editForm.lastName?.trim() &&
      !!this.editForm.email?.trim();
  }

  cancelEdit(): void { this.editMode.set(false); this.roleDropdownOpen.set(false); }

  saveEdit(): void {
    this.editForm.dateOfBirth = this.editForm.dateOfBirth || null;
    this.saving.set(true);
    this.staffStore.update(this.staffId, this.editForm).subscribe({
      next:  ()       => { this.loadStaff(); this.editMode.set(false); this.saving.set(false); this.toast.success('Profile updated.'); },
      error: (e: any) => { this.toast.error(e?.error?.error ?? 'Update failed.'); this.saving.set(false); },
    });
  }
}
