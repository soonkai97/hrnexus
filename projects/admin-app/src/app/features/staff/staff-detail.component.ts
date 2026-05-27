import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { StaffDetail, UpdateStaffRequest, StaffStore, StaffManagementService, AuthService, ToastService, WorkLogService, WorkLogDto, AmPmPipe } from 'shared-lib';
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
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, TranslatePipe, AmPmPipe,
    Button, InputText, TableModule, Tag, Card, Skeleton, DatePicker, MultiSelect, Paginator],
  templateUrl: './staff-detail.component.html',
  styleUrl: './staff-detail.component.scss',
})
export class StaffDetailComponent implements OnInit {
  readonly staffStore  = inject(StaffStore);
  private readonly auth       = inject(AuthService);
  private readonly staffSvc   = inject(StaffManagementService);
  private readonly toast      = inject(ToastService);
  private readonly route      = inject(ActivatedRoute);
  private readonly workLogSvc = inject(WorkLogService);

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
    this.editMode.set(true);
  }

  /** Gates the Save button — only Staff ID, first and last name are required. */
  get isEditFormValid(): boolean {
    return !!this.editForm.staffId?.trim() &&
      !!this.editForm.firstName?.trim();
  }

  cancelEdit(): void { this.editMode.set(false); }

  saveEdit(): void {
    this.saving.set(true);
    this.staffStore.update(this.staffId, this.editForm).subscribe({
      next:  ()       => { this.loadStaff(); this.editMode.set(false); this.saving.set(false); this.toast.success('Profile updated.'); },
      error: (e: any) => { this.toast.error(e?.error?.error ?? 'Update failed.'); this.saving.set(false); },
    });
  }
}
