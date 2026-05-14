import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LeaveRecord, UpsertLeaveRequest, LeaveBalance, StaffLeaveBalance, LeaveService, StaffStore, ToastService, ConfirmService, TooltipDirective, toLocalDateString } from 'shared-lib';

const LEAVE_TYPES = ['sick', 'annual', 'unpaid', 'no-show'] as const;
const STATUSES    = ['approved', 'pending', 'rejected'] as const;

@Component({
  selector: 'app-leave',
  standalone: true,
  imports: [CommonModule, FormsModule, TitleCasePipe, TranslatePipe, TooltipDirective],
  templateUrl: './leave.component.html',
  styleUrl: './leave.component.scss',
})
export class LeaveComponent implements OnInit {
  readonly staffStore  = inject(StaffStore);
  private readonly leaveSvc   = inject(LeaveService);
  private readonly toast      = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);

  @ViewChild('formAnchor') private formAnchor?: ElementRef<HTMLElement>;

  records  = signal<LeaveRecord[]>([]);
  loading  = signal(false);
  saving   = signal(false);
  showForm = signal(false);
  editing  = signal<string | null>(null);
  formError = signal('');

  /** Pagination */
  readonly pageSize  = 10;
  currentPage        = signal(1);
  totalRecords       = signal(0);
  totalPages         = computed(() => Math.max(1, Math.ceil(this.totalRecords() / this.pageSize)));

  /** Top-level tab */
  tab              = signal<'records' | 'balances'>('records');
  totalPendingCount = signal(0);

  filterEmployeeId = '';
  filterFrom       = '';
  filterTo         = '';
  filterStatus     = '';

  /** Overdue pending leaves (loaded separately, all pages) */
  overdueLeaves    = signal<LeaveRecord[]>([]);
  overdueExpanded  = signal(true);

  staffBalance       = signal<LeaveBalance | null>(null);
  allBalances        = signal<StaffLeaveBalance[]>([]);
  allBalancesLoading = signal(false);
  editingEntitlement = signal(false);
  savingEntitlement  = signal(false);
  entitlementForm    = { year: new Date().getFullYear(), annualDays: 14, sickDays: 14 };

  readonly currentYear = new Date().getFullYear();
  readonly leaveTypes  = LEAVE_TYPES;
  readonly calDayKeys  = ['leave.calDayMon','leave.calDayTue','leave.calDayWed','leave.calDayThu','leave.calDayFri','leave.calDaySat','leave.calDaySun'];

  // ── Calendar view ────────────────────────────────────────────────────────
  viewMode    = signal<'table' | 'calendar'>('table');
  calYear     = signal(new Date().getFullYear());
  calMonth    = signal(new Date().getMonth()); // 0-based

  calMonthName = computed(() =>
    new Date(this.calYear(), this.calMonth(), 1).toLocaleString('en-US', { month: 'long', year: 'numeric' }));

  calDays = computed(() => {
    const year  = this.calYear();
    const month = this.calMonth();
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const recs  = this.records();
    return Array.from({ length: daysInMonth }, (_, i) => {
      const day  = i + 1;
      const date = new Date(Date.UTC(year, month, day));
      const iso  = date.toISOString().slice(0, 10);
      const leaves = recs.filter(r => {
        const from = r.startDate.slice(0, 10);
        const to   = r.endDate.slice(0, 10);
        return iso >= from && iso <= to;
      });
      return { day, iso, isWeekend: date.getUTCDay() === 0 || date.getUTCDay() === 6, leaves };
    });
  });

  prevCalMonth(): void {
    if (this.calMonth() === 0) { this.calMonth.set(11); this.calYear.update(y => y - 1); }
    else                         this.calMonth.update(m => m - 1);
    this.loadCalRecords();
  }

  nextCalMonth(): void {
    if (this.calMonth() === 11) { this.calMonth.set(0); this.calYear.update(y => y + 1); }
    else                          this.calMonth.update(m => m + 1);
    this.loadCalRecords();
  }

  loadCalRecords(): void {
    const year  = this.calYear();
    const month = this.calMonth();
    const fmt   = (d: Date) => d.toISOString().slice(0, 10);
    const from  = fmt(new Date(Date.UTC(year, month, 1)));
    const to    = fmt(new Date(Date.UTC(year, month + 1, 0)));
    this.selectedCalDay.set(null);
    this.loading.set(true);
    // Calendar only shows one month — 200 is a safe upper bound
    this.leaveSvc.getAll({ dateFrom: from, dateTo: to, status: this.filterStatus || undefined, page: 1, pageSize: 200 }).subscribe({
      next:  r  => { this.records.set(r?.items ?? []); this.loading.set(false); },
      error: () => { this.toast.error('Failed to load leave records.'); this.loading.set(false); },
    });
  }

  readonly todayIso = toLocalDateString(new Date());

  /** Calendar day detail panel */
  selectedCalDay = signal<string | null>(null);
  selectedCalDayLeaves = computed(() => {
    const iso = this.selectedCalDay();
    if (!iso) return [];
    return this.records().filter(r => {
      const from = r.startDate.slice(0, 10);
      const to   = r.endDate.slice(0, 10);
      return iso >= from && iso <= to;
    });
  });

  selectCalDay(iso: string): void {
    this.selectedCalDay.set(this.selectedCalDay() === iso ? null : iso);
  }

  openCalLeave(r: LeaveRecord, event: Event): void {
    event.stopPropagation();
    this.startEdit(r);
  }

  /** Number of blank cells before day 1 (Monday-based grid). */
  leadingBlanks = computed(() => {
    const d = new Date(this.calYear(), this.calMonth(), 1).getDay(); // 0=Sun
    return Array(d === 0 ? 6 : d - 1).fill(0);
  });

  switchView(mode: 'table' | 'calendar'): void {
    this.viewMode.set(mode);
    if (mode === 'calendar') this.loadCalRecords();
  }
  readonly statuses   = STATUSES;
  readonly presets    = [
    { labelKey: 'common.today',     value: 'today'  as const },
    { labelKey: 'common.thisWeek',  value: 'week'   as const },
    { labelKey: 'common.thisMonth', value: 'month'  as const },
  ];

  form: UpsertLeaveRequest = {
    employeeId: '', leaveType: 'sick', startDate: '', endDate: '', notes: null, status: 'approved',
  };

  ngOnInit(): void {
    this.staffStore.loadStaff();
    this.load();
    this.refreshPendingCount();
    this.loadOverdue();
  }

  loadOverdue(): void {
    this.leaveSvc.getAll({ status: 'pending', pageSize: 200 }).subscribe({
      next: r => {
        const today = this.todayIso;
        this.overdueLeaves.set((r?.items ?? []).filter(l => l.endDate < today));
      },
    });
  }

  markNoShow(r: LeaveRecord): void {
    const req: UpsertLeaveRequest = {
      employeeId: r.employeeId, leaveType: 'no-show',
      startDate: r.startDate, endDate: r.endDate,
      notes: r.notes, status: 'approved',
    };
    this.leaveSvc.update(r.id, req).subscribe({
      next:  () => { this.load(); this.loadOverdue(); this.refreshPendingCount(); this.toast.success('Marked as no-show.'); },
      error: () => this.toast.error('Update failed.'),
    });
  }

  /** Fetch the true total-pending count independent of pagination. */
  refreshPendingCount(): void {
    this.leaveSvc.getAll({ status: 'pending', pageSize: 1 }).subscribe({
      next: r => this.totalPendingCount.set(r?.total ?? 0),
    });
  }

  setPreset(preset: 'today' | 'week' | 'month'): void {
    const fmt = (d: Date) => toLocalDateString(d);
    const now = new Date();
    switch (preset) {
      case 'today': this.filterFrom = this.filterTo = fmt(now); break;
      case 'week': {
        const mon = new Date(now);
        mon.setDate(now.getDate() - ((now.getDay() + 6) % 7));
        this.filterFrom = fmt(mon); this.filterTo = fmt(now); break;
      }
      case 'month': {
        const first = new Date(now.getFullYear(), now.getMonth(), 1);
        this.filterFrom = fmt(first); this.filterTo = fmt(now); break;
      }
    }
    this.load(1);
  }

  load(page = 1): void {
    this.loading.set(true);
    this.currentPage.set(page);
    this.leaveSvc.getAll({
      employeeId: this.filterEmployeeId || undefined,
      dateFrom:   this.filterFrom   || undefined,
      dateTo:     this.filterTo     || undefined,
      status:     this.filterStatus || undefined,
      page,
      pageSize:   this.pageSize,
    }).subscribe({
      next:  r  => {
        this.records.set(r?.items ?? []);
        this.totalRecords.set(r?.total ?? 0);
        this.loading.set(false);
      },
      error: () => { this.toast.error('Failed to load leave records.'); this.loading.set(false); },
    });

    if (this.filterEmployeeId) {
      this.allBalances.set([]);
      this.leaveSvc.getBalance(this.filterEmployeeId).subscribe({
        next: b => {
          this.staffBalance.set(b);
          this.entitlementForm = { year: b.year, annualDays: b.annualEntitlement, sickDays: b.sickEntitlement };
        },
        error: () => this.staffBalance.set(null),
      });
    } else {
      this.staffBalance.set(null);
      this.allBalancesLoading.set(true);
      this.leaveSvc.getAllBalances().subscribe({
        next:  b  => { this.allBalances.set(b); this.allBalancesLoading.set(false); },
        error: () => { this.allBalances.set([]); this.allBalancesLoading.set(false); },
      });
    }
  }

  toggleEntitlementEdit(): void { this.editingEntitlement.update(v => !v); }

  saveEntitlement(): void {
    if (!this.filterEmployeeId) return;
    this.savingEntitlement.set(true);
    this.leaveSvc.setEntitlement(
      this.filterEmployeeId,
      this.entitlementForm.year,
      this.entitlementForm.annualDays,
      this.entitlementForm.sickDays,
    ).subscribe({
      next: () => {
        this.savingEntitlement.set(false);
        this.editingEntitlement.set(false);
        this.toast.success('Entitlement updated.');
        this.load();
      },
      error: () => { this.toast.error('Failed to update entitlement.'); this.savingEntitlement.set(false); },
    });
  }

  quickStatus(id: string, status: 'approved' | 'rejected'): void {
    const r = this.records().find(r => r.id === id);
    if (!r) return;
    const req: UpsertLeaveRequest = {
      employeeId: r.employeeId, leaveType: r.leaveType as any,
      startDate: r.startDate, endDate: r.endDate,
      notes: r.notes, status,
    };
    this.leaveSvc.update(id, req).subscribe({
      next:  () => { this.load(); this.loadOverdue(); this.refreshPendingCount(); this.toast.success(`Leave ${status}.`); },
      error: () => this.toast.error('Update failed.'),
    });
  }

  countByType(type: string): number {
    return this.records()
      .filter(r => r.leaveType === type && r.status === 'approved')
      .reduce((s, r) => s + r.days, 0);
  }

  private scrollToForm(): void {
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  openAdd(): void {
    this.form = { employeeId: '', leaveType: 'sick',
      startDate: toLocalDateString(new Date()),
      endDate:   toLocalDateString(new Date()),
      notes: null, status: 'approved' };
    this.editing.set(null);
    this.formError.set('');
    this.showForm.set(true);
    this.scrollToForm();
  }

  startEdit(r: LeaveRecord): void {
    this.form = { employeeId: r.employeeId, leaveType: r.leaveType as any,
      startDate: r.startDate, endDate: r.endDate,
      notes: r.notes, status: r.status as any };
    this.editing.set(r.id);
    this.formError.set('');
    this.showForm.set(true);
    this.scrollToForm();
  }

  cancelForm(): void { this.showForm.set(false); this.editing.set(null); }

  save(): void {
    if (!this.form.employeeId || !this.form.startDate || !this.form.endDate) {
      this.formError.set('Please fill in all required fields.'); return;
    }
    this.saving.set(true);
    const id   = this.editing();
    const fail = (e: any) => { this.formError.set(e?.error?.error ?? 'Save failed.'); this.saving.set(false); };
    if (id) {
      this.leaveSvc.update(id, this.form).subscribe({
        next: () => { this.saving.set(false); this.cancelForm(); this.load(); this.loadOverdue(); this.refreshPendingCount(); this.toast.success('Leave saved.'); },
        error: fail,
      });
    } else {
      this.leaveSvc.create(this.form).subscribe({
        next: (res) => {
          this.saving.set(false);
          this.cancelForm();
          this.load();
          this.loadOverdue();
          this.refreshPendingCount();
          this.toast.success('Leave saved.');
        },
        error: fail,
      });
    }
  }


  remove(id: string): void {
    this.confirmSvc.confirm({ title: 'Delete Leave', message: 'Remove this leave record?', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.leaveSvc.delete(id).subscribe({
          next:  () => { this.load(); this.loadOverdue(); this.refreshPendingCount(); this.toast.success('Leave deleted.'); },
          error: () => this.toast.error('Delete failed.'),
        });
      });
  }
}
