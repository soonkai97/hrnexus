import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef, DestroyRef } from '@angular/core';
import { Subject, EMPTY } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import {
  LeaveRecord, UpsertLeaveRequest, LeaveBalance, StaffLeaveBalance,
  LeaveSummary, LeavePatternWarning,
  LeaveService, StaffStore, ToastService, ConfirmService, toLocalDateString,
  TrimInputDirective, CanComponentDeactivate,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { ProgressBar } from 'primeng/progressbar';
import { InputText } from 'primeng/inputtext';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { Paginator } from 'primeng/paginator';
import { InputNumber } from 'primeng/inputnumber';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { Checkbox } from 'primeng/checkbox';

const LEAVE_TYPES = ['sick', 'annual', 'unpaid', 'no-show', 'emergency'] as const;
const STATUSES    = ['approved', 'pending', 'rejected', 'cancelled'] as const;

@Component({
  selector: 'app-leave',
  standalone: true,
  imports: [DatePipe, TitleCasePipe, FormsModule, TranslatePipe, TrimInputDirective,
    Button, Tag, ProgressBar, InputText, InputNumber, Card, TooltipModule,
    Message, Select, Skeleton, DatePicker, Tabs, TabList, Tab, Paginator, Checkbox],
  templateUrl: './leave.component.html',
  styleUrl: './leave.component.scss',
})
export class LeaveComponent implements OnInit, CanComponentDeactivate {
  readonly staffStore  = inject(StaffStore);
  private readonly leaveSvc   = inject(LeaveService);
  private readonly toast      = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);

  // switchMap cancels any in-flight getAll request when a newer load(page) call arrives —
  // prevents stale page data appearing after rapid filter changes or pagination clicks.
  private readonly _loadTrigger$ = new Subject<number>();

  constructor() {
    this._loadTrigger$.pipe(
      switchMap(page => {
        this.loading.set(true);
        this.currentPage.set(page);
        return this.leaveSvc.getAll({
          employeeId: this.filterEmployeeId || undefined,
          dateFrom:   this.filterFrom       || undefined,
          dateTo:     this.filterTo         || undefined,
          status:     this.filterStatus     || undefined,
          leaveType:  this.filterLeaveType  || undefined,
          page,
          pageSize:   this.pageSize,
        }).pipe(
          catchError(() => {
            this.toast.error('Failed to load leave records.');
            this.loading.set(false);
            return EMPTY;
          }),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(r => {
      this.records.set(r?.items ?? []);
      this.totalRecords.set(r?.total ?? 0);
      this.loading.set(false);
      this.loadBalances();
    });
  }

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;

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
  filterLeaveType  = '';

  readonly presetTypes = [
    { label: 'All',       value: '' },
    { label: 'Sick',      value: 'sick' },
    { label: 'Annual',    value: 'annual' },
    { label: 'Unpaid',    value: 'unpaid' },
    { label: 'Emergency', value: 'emergency' },
    { label: 'No-Show',   value: 'no-show' },   // item 4
  ];

  setTypeFilter(type: string): void {
    this.filterLeaveType = type;
    if (this.viewMode() === 'calendar') this.loadCalRecords();
    else this.load();
  }

  readonly datePresets = [
    { label: 'Today',         value: 'today'         as const },
    { label: 'This Week',     value: 'week'          as const },
    { label: 'This Month',    value: 'month'         as const },
    { label: 'Last Month',    value: 'last-month'    as const },
    { label: 'Last 3 Months', value: 'last-3-months' as const },
  ];

  setDatePreset(preset: 'today' | 'week' | 'month' | 'last-month' | 'last-3-months'): void {
    const now = new Date();
    const fmt = (d: Date) => toLocalDateString(d);
    switch (preset) {
      case 'today':
        this.filterFrom = this.filterTo = fmt(now); break;
      case 'week': {
        const mon = new Date(now);
        mon.setDate(now.getDate() - ((now.getDay() + 6) % 7));
        this.filterFrom = fmt(mon); this.filterTo = fmt(now); break;
      }
      case 'month':
        this.filterFrom = fmt(new Date(now.getFullYear(), now.getMonth(), 1));
        this.filterTo   = fmt(now); break;
      case 'last-month': {
        const first = new Date(now.getFullYear(), now.getMonth() - 1, 1);
        const last  = new Date(now.getFullYear(), now.getMonth(), 0);
        this.filterFrom = fmt(first); this.filterTo = fmt(last); break;
      }
      case 'last-3-months': {
        const first3 = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        this.filterFrom = fmt(first3); this.filterTo = fmt(now); break;
      }
    }
    this.load(1);
  }

  /** Overdue pending leaves (loaded separately, all pages) */
  overdueLeaves    = signal<LeaveRecord[]>([]);
  overdueExpanded  = signal(true);

  staffBalance       = signal<LeaveBalance | null>(null);
  formBalance        = signal<LeaveBalance | null>(null);
  allBalances        = signal<StaffLeaveBalance[]>([]);
  allBalancesLoading = signal(false);
  editingEntitlement = signal(false);
  savingEntitlement  = signal(false);
  bulkSaving         = signal(false);
  entitlementForm    = { year: new Date().getFullYear(), annualHours: 112, sickHours: 112 };
  showBulkEntForm    = signal(false);

  /** Item 12: year used for the Balances overview */
  balanceYear = signal(new Date().getFullYear());

  /** Item 11: leave summary + pattern warnings */
  leaveSummary         = signal<LeaveSummary[]>([]);
  leaveSummaryLoading  = signal(false);
  patternWarnings      = signal<LeavePatternWarning[]>([]);
  patternLoading       = signal(false);

  /** Item 3: bulk selection */
  bulkSelecting    = signal(false);
  selectedIds      = signal<Set<string>>(new Set());
  bulkRejReason    = signal('');
  bulkStatusSaving = signal(false);

  selectedCount = computed(() => this.selectedIds().size);
  allVisibleSelected = computed(() => {
    const ids = this.selectedIds();
    const pending = this.records().filter(r => r.status === 'pending');
    return pending.length > 0 && pending.every(r => ids.has(r.id));
  });

  toggleBulkMode(): void {
    this.bulkSelecting.update(v => !v);
    if (!this.bulkSelecting()) { this.selectedIds.set(new Set()); this.bulkRejReason.set(''); }
  }

  toggleSelectAll(): void {
    const pending = this.records().filter(r => r.status === 'pending');
    if (this.allVisibleSelected()) {
      const next = new Set(this.selectedIds());
      pending.forEach(r => next.delete(r.id));
      this.selectedIds.set(next);
    } else {
      const next = new Set(this.selectedIds());
      pending.forEach(r => next.add(r.id));
      this.selectedIds.set(next);
    }
  }

  toggleSelect(id: string): void {
    const next = new Set(this.selectedIds());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.selectedIds.set(next);
  }

  bulkApproveSelected(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length) return;
    this.confirmSvc.confirm({
      title: `Approve ${ids.length} Leave Request(s)`,
      message: `Approve all ${ids.length} selected leave request(s)?`,
      confirmLabel: 'Approve All',
    }).subscribe(ok => {
      if (!ok) return;
      this.bulkStatusSaving.set(true);
      this.leaveSvc.bulkUpdateStatus(ids, 'approved').subscribe({
        next: r => {
          this.bulkStatusSaving.set(false);
          this.toast.success(`${r.count} leave request(s) approved.`);
          this.selectedIds.set(new Set());
          this.load(); this.loadOverdue(); this.refreshPendingCount(); this.loadBalances();
        },
        error: (e: any) => { this.toast.error(e?.error?.error ?? 'Bulk approve failed.'); this.bulkStatusSaving.set(false); },
      });
    });
  }

  bulkRejectSelected(): void {
    const ids = [...this.selectedIds()];
    if (!ids.length) return;
    const reason = this.bulkRejReason().trim() || undefined;
    this.confirmSvc.confirm({
      title: `Reject ${ids.length} Leave Request(s)`,
      message: `Reject all ${ids.length} selected leave request(s)?${reason ? ` Reason: "${reason}"` : ''}`,
      confirmLabel: 'Reject All',
      danger: true,
    }).subscribe(ok => {
      if (!ok) return;
      this.bulkStatusSaving.set(true);
      this.leaveSvc.bulkUpdateStatus(ids, 'rejected', reason).subscribe({
        next: r => {
          this.bulkStatusSaving.set(false);
          this.toast.success(`${r.count} leave request(s) rejected.`);
          this.selectedIds.set(new Set()); this.bulkRejReason.set('');
          this.load(); this.loadOverdue(); this.refreshPendingCount(); this.loadBalances();
        },
        error: (e: any) => { this.toast.error(e?.error?.error ?? 'Bulk reject failed.'); this.bulkStatusSaving.set(false); },
      });
    });
  }

  processingId    = signal<string | null>(null);
  deletingLeaveId = signal<string | null>(null);

  /** Item 1: inline rejection-reason flow */
  rejectingId     = signal<string | null>(null);
  rejectionInput  = signal('');

  startReject(r: LeaveRecord): void {
    this.rejectingId.set(r.id);
    this.rejectionInput.set('');
  }

  cancelReject(): void { this.rejectingId.set(null); this.rejectionInput.set(''); }

  confirmReject(id: string): void {
    const reason = this.rejectionInput().trim() || undefined;
    this.rejectingId.set(null);
    this.rejectionInput.set('');
    this._doQuickStatus(id, 'rejected', reason);
  }

  readonly currentYear = new Date().getFullYear();
  readonly leaveTypes: string[] = [...LEAVE_TYPES];
  readonly leaveTypeOptions = LEAVE_TYPES.map(t => ({
    label: t === 'no-show' ? 'No-Show' : t.charAt(0).toUpperCase() + t.slice(1),
    value: t,
  }));
  readonly calDayKeys  = ['leave.calDayMon','leave.calDayTue','leave.calDayWed','leave.calDayThu','leave.calDayFri','leave.calDaySat','leave.calDaySun'];

  readonly tabOptions = [
    { labelKey: 'leave.recordsTab',  value: 'records'  },
    { labelKey: 'leave.balancesTab', value: 'balances' },
  ];
  readonly viewModeOptions = [
    { label: 'Table',    value: 'table'    },
    { label: 'Calendar', value: 'calendar' },
  ];

  // ── Calendar view ────────────────────────────────────────────────────────
  viewMode    = signal<'table' | 'calendar'>('table');
  calYear     = signal(new Date().getFullYear());
  calMonth    = signal(new Date().getMonth());

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
    this.leaveSvc.getAll({ dateFrom: from, dateTo: to, status: this.filterStatus || undefined, leaveType: this.filterLeaveType || undefined, page: 1, pageSize: 200 }).subscribe({
      next:  r  => { this.records.set(r?.items ?? []); this.loading.set(false); },
      error: () => { this.toast.error('Failed to load leave records.'); this.loading.set(false); },
    });
  }

  readonly todayIso = toLocalDateString(new Date());

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
    // Select the day to reveal the detail panel — do not jump straight into edit
    this.selectedCalDay.set(r.startDate.slice(0, 10));
  }

  leadingBlanks = computed(() => {
    const d = new Date(this.calYear(), this.calMonth(), 1).getDay();
    return Array(d === 0 ? 6 : d - 1).fill(0);
  });

  switchView(mode: 'table' | 'calendar'): void {
    this.viewMode.set(mode);
    if (mode === 'calendar') this.loadCalRecords();
  }

  readonly statuses: string[] = [...STATUSES];
  readonly statusOptions = STATUSES.map(s => ({
    label: s === 'cancelled' ? 'Cancelled' : s.charAt(0).toUpperCase() + s.slice(1),
    value: s,
  }));

  form: UpsertLeaveRequest = {
    employeeId: '', leaveType: 'sick', startDate: '', endDate: '',
    startTime: null, endTime: null, hours: null, notes: null, status: 'approved',
  };

  /** Intermediary Date bindings for the time pickers — converted to string on save(). */
  startTimeDate: Date | null = null;
  endTimeDate:   Date | null = null;

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  private parseTime(t: string | null | undefined): Date | null {
    if (!t) return null;
    const [h, m] = t.split(':').map(Number);
    const d = new Date(); d.setHours(h, m, 0, 0); return d;
  }

  get calcHours(): number {
    const { startDate, endDate } = this.form;
    const st = this.startTimeDate;
    const et = this.endTimeDate;
    if (!startDate || !st || !endDate || !et) return 0;
    const start = new Date(startDate);
    start.setHours(st.getHours(), st.getMinutes(), 0, 0);
    const end = new Date(endDate);
    end.setHours(et.getHours(), et.getMinutes(), 0, 0);
    const h = (end.getTime() - start.getTime()) / (1000 * 60 * 60);
    return Math.round(h * 10) / 10;
  }

  onStartDateChange(date: string): void {
    if (!this.form.endDate || this.form.endDate < date) {
      this.form.endDate = date;
    }
  }

  ngOnInit(): void {
    this.staffStore.loadStaff();
    this.load();
    this.refreshPendingCount();
    this.loadOverdue();
    this.loadPatternWarnings();
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
      startTime: r.startTime, endTime: r.endTime, hours: r.hours,
      notes: r.notes, status: 'approved',
    };
    this.processingId.set(r.id);
    this.leaveSvc.update(r.id, req).subscribe({
      next:  () => {
        this.processingId.set(null);
        this.load(); this.loadOverdue(); this.refreshPendingCount();
        this.loadBalances();   // item 15
        this.toast.success('Marked as no-show.');
      },
      error: () => { this.processingId.set(null); this.toast.error('Update failed.'); },
    });
  }

  refreshPendingCount(): void {
    this.leaveSvc.getAll({ status: 'pending', pageSize: 1 }).subscribe({
      next: r => this.totalPendingCount.set(r?.total ?? 0),
    });
  }

  load(page = 1): void {
    this._loadTrigger$.next(page);
  }

  loadBalances(): void {
    const year = this.balanceYear();
    if (this.filterEmployeeId) {
      this.allBalances.set([]);
      this.leaveSvc.getBalance(this.filterEmployeeId, year).subscribe({
        next: b => {
          this.staffBalance.set(b);
          this.entitlementForm = { year: b.year, annualHours: b.annualEntitlement, sickHours: b.sickEntitlement };
        },
        error: () => this.staffBalance.set(null),
      });
    } else {
      this.staffBalance.set(null);
      this.allBalancesLoading.set(true);
      this.leaveSvc.getAllBalances(year).subscribe({
        next:  b  => { this.allBalances.set(b); this.allBalancesLoading.set(false); },
        error: () => { this.allBalances.set([]); this.allBalancesLoading.set(false); },
      });
    }
  }

  /** Item 11: load leave summary for Balances tab */
  loadLeaveSummary(): void {
    this.leaveSummaryLoading.set(true);
    this.leaveSvc.getSummary(this.balanceYear()).subscribe({
      next:  s => { this.leaveSummary.set(s); this.leaveSummaryLoading.set(false); },
      error: () => this.leaveSummaryLoading.set(false),
    });
  }

  /** Item 12: load sick-leave pattern warnings */
  loadPatternWarnings(): void {
    this.patternLoading.set(true);
    this.leaveSvc.getPatternWarnings().subscribe({
      next:  w => { this.patternWarnings.set(w); this.patternLoading.set(false); },
      error: () => this.patternLoading.set(false),
    });
  }

  prevBalanceYear(): void { this.balanceYear.update(y => y - 1); this.loadBalances(); this.loadLeaveSummary(); }
  nextBalanceYear(): void { this.balanceYear.update(y => y + 1); this.loadBalances(); this.loadLeaveSummary(); }

  toggleEntitlementEdit(): void { this.editingEntitlement.update(v => !v); }

  saveEntitlement(): void {
    if (!this.filterEmployeeId) return;
    this.savingEntitlement.set(true);
    this.leaveSvc.setEntitlement(
      this.filterEmployeeId,
      this.entitlementForm.year,
      this.entitlementForm.annualHours,
      this.entitlementForm.sickHours,
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

  bulkApplyEntitlement(): void {
    const { year, annualHours, sickHours } = this.entitlementForm;
    this.confirmSvc.confirm({
      title:        'Apply to All Staff',
      message:      `Set ${annualHours}h annual + ${sickHours}h sick leave for ALL active staff for ${year}? Existing entitlements will be overwritten.`,
      confirmLabel: 'Apply to All',
    }).subscribe(ok => {
      if (!ok) return;
      this.bulkSaving.set(true);
      this.leaveSvc.bulkSetEntitlement(year, annualHours, sickHours).subscribe({
        next: r => {
          this.bulkSaving.set(false);
          this.showBulkEntForm.set(false);
          this.toast.success(`Entitlements applied to ${r.count} staff members.`);
          this.loadBalances();
        },
        error: (e: any) => {
          this.toast.error(e?.error?.error ?? 'Bulk apply failed.');
          this.bulkSaving.set(false);
        },
      });
    });
  }

  onFormEmployeeChange(empId: string): void {
    if (!empId) { this.formBalance.set(null); return; }
    this.leaveSvc.getBalance(empId).subscribe({
      next:  b  => this.formBalance.set(b),
      error: () => this.formBalance.set(null),
    });
  }

  exportCsv(): void {
    this.leaveSvc.getAll({
      employeeId: this.filterEmployeeId || undefined,
      dateFrom:   this.filterFrom       || undefined,
      dateTo:     this.filterTo         || undefined,
      status:     this.filterStatus     || undefined,
      leaveType:  this.filterLeaveType  || undefined,
      page: 1, pageSize: 1000,
    }).subscribe({
      next: r => {
        const rows = r?.items ?? [];
        if (rows.length === 0) { this.toast.warning('No records to export.'); return; }
        const escape = (v: string | number | null | undefined) => {
          const s = String(v ?? '');
          return s.includes(',') || s.includes('"') || s.includes('\n')
            ? `"${s.replace(/"/g, '""')}"` : s;
        };
        const header = 'Staff ID,Name,Leave Type,Start Date,End Date,Hours,Status,Notes,Rejection Reason,Created At';
        const body = rows.map(row =>
          [row.staffId, row.staffName, row.leaveType, row.startDate, row.endDate,
           row.hours, row.status, row.notes ?? '', row.rejectionReason ?? '', row.createdAt]
          .map(escape).join(',')
        ).join('\n');
        const blob = new Blob([header + '\n' + body], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const a   = document.createElement('a');
        a.href = url;
        a.download = `leave-export-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        this.toast.success(`Exported ${rows.length} records.`);
      },
      error: () => this.toast.error('Export failed.'),
    });
  }

  /** Item 15: quickStatus now also reloads balances */
  quickStatus(id: string, status: 'approved' | 'rejected'): void {
    if (status === 'rejected') { this.startReject(this.records().find(r => r.id === id)!); return; }
    this._doQuickStatus(id, status);
  }

  private _doQuickStatus(id: string, status: 'approved' | 'rejected', rejectionReason?: string): void {
    const r = this.records().find(r => r.id === id);
    if (!r) return;
    const req: UpsertLeaveRequest = {
      employeeId: r.employeeId, leaveType: r.leaveType as any,
      startDate: r.startDate, endDate: r.endDate,
      startTime: r.startTime, endTime: r.endTime, hours: r.hours,
      notes: r.notes, status, rejectionReason: rejectionReason ?? null,
    };
    this.processingId.set(id);
    this.leaveSvc.update(id, req).subscribe({
      next:  () => {
        this.processingId.set(null);
        this.load(); this.loadOverdue(); this.refreshPendingCount();
        this.loadBalances();  // item 15
        this.toast.success(`Leave ${status}.`);
      },
      error: () => { this.processingId.set(null); this.toast.error('Update failed.'); },
    });
  }

  leaveTypeSeverity(type: string): 'success' | 'danger' | 'secondary' | 'warn' {
    const map: Record<string, 'success' | 'danger' | 'secondary' | 'warn'> = {
      annual:    'success',
      sick:      'danger',
      unpaid:    'secondary',
      'no-show': 'warn',
      emergency: 'warn',
    };
    return map[type] ?? 'secondary';
  }

  /** Item 10: cancelled maps to 'secondary', not 'danger' */
  statusSeverity(status: string): 'success' | 'warn' | 'danger' | 'secondary' {
    const map: Record<string, 'success' | 'warn' | 'danger' | 'secondary'> = {
      approved:  'success',
      pending:   'warn',
      rejected:  'danger',
      cancelled: 'secondary',
    };
    return map[status] ?? 'secondary';
  }

  countByType(type: string): number {
    return this.records()
      .filter(r => r.leaveType === type && r.status === 'approved')
      .reduce((s, r) => s + r.hours, 0);
  }

  /** Item 12: check if an employee has a pattern warning */
  patternWarningFor(employeeId: string): LeavePatternWarning | undefined {
    return this.patternWarnings().find(w => w.employeeId === employeeId);
  }

  hasUnsavedChanges(): boolean {
    return this.showForm() && !!this.form.employeeId;
  }

  private scrollToForm(): void {
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  openAdd(): void {
    this.form = { employeeId: '', leaveType: 'sick',
      startDate: toLocalDateString(new Date()),
      endDate:   toLocalDateString(new Date()),
      startTime: null, endTime: null, hours: null,
      notes: null, status: 'approved' };
    this.startTimeDate = null;
    this.endTimeDate   = null;
    this.formBalance.set(null);
    this.editing.set(null);
    this.formError.set('');
    this.showForm.set(true);
    this.scrollToForm();
  }

  startEdit(r: LeaveRecord): void {
    this.form = { employeeId: r.employeeId, leaveType: r.leaveType as any,
      startDate: r.startDate, endDate: r.endDate,
      startTime: r.startTime, endTime: r.endTime, hours: r.hours,
      notes: r.notes, status: r.status as any,
      rejectionReason: r.rejectionReason };
    this.startTimeDate = this.parseTime(r.startTime);
    this.endTimeDate   = this.parseTime(r.endTime);
    this.editing.set(r.id);
    this.formError.set('');
    this.formBalance.set(null);
    this.leaveSvc.getBalance(r.employeeId).subscribe({
      next:  b  => this.formBalance.set(b),
      error: () => {},
    });
    this.showForm.set(true);
    this.scrollToForm();
  }

  cancelForm(): void {
    this.showForm.set(false);
    this.editing.set(null);
    this.formBalance.set(null);
    this.startTimeDate = null;
    this.endTimeDate   = null;
  }

  /** Save is enabled only once all required fields are filled. The date/time-order check
   *  stays in save() so the user gets a specific reason rather than a silently-disabled button. */
  get isFormValid(): boolean {
    return !!this.form.employeeId && !!this.form.startDate && !!this.form.endDate
        && !!this.startTimeDate && !!this.endTimeDate;
  }

  save(): void {
    this.form.startTime = this.startTimeDate ? this.toTimeStr(this.startTimeDate) : null;
    this.form.endTime   = this.endTimeDate   ? this.toTimeStr(this.endTimeDate)   : null;

    if (!this.form.employeeId || !this.form.startDate || !this.form.endDate
        || !this.startTimeDate || !this.endTimeDate) {
      this.formError.set('Please fill in all required fields.'); return;
    }
    const h = this.calcHours;
    if (h <= 0) { this.formError.set('End date/time must be after start date/time.'); return; }
    this.form.hours = h;

    this.saving.set(true);
    const id   = this.editing();
    const fail = (e: any) => { this.formError.set(e?.error?.error ?? 'Save failed.'); this.saving.set(false); };
    if (id) {
      this.leaveSvc.update(id, this.form).subscribe({
        next: () => {
          this.saving.set(false); this.cancelForm();
          this.load(); this.loadOverdue(); this.refreshPendingCount();
          this.toast.success('Leave saved.');
        },
        error: fail,
      });
    } else {
      this.leaveSvc.create(this.form).subscribe({
        next: () => {
          this.saving.set(false); this.cancelForm();
          this.load(); this.loadOverdue(); this.refreshPendingCount();
          this.toast.success('Leave saved.');
        },
        error: fail,
      });
    }
  }

  remove(id: string): void {
    this.confirmSvc.confirm({ title: 'Delete Leave Record', message: 'Remove this leave record? This cannot be undone.', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.deletingLeaveId.set(id);
        this.leaveSvc.delete(id).subscribe({
          next:  () => {
            this.deletingLeaveId.set(null);
            this.load(); this.loadOverdue(); this.refreshPendingCount();
            this.loadBalances();   // item 15
            this.toast.success('Leave deleted.');
          },
          error: () => { this.deletingLeaveId.set(null); this.toast.error('Delete failed.'); },
        });
      });
  }
}
