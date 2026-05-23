import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { DatePipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LeaveRecord, UpsertLeaveRequest, LeaveBalance, StaffLeaveBalance, LeaveService, StaffStore, ToastService, ConfirmService, toLocalDateString } from 'shared-lib';
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

const LEAVE_TYPES = ['sick', 'annual', 'unpaid', 'no-show', 'emergency'] as const;
const STATUSES    = ['approved', 'pending', 'rejected'] as const;

@Component({
  selector: 'app-leave',
  standalone: true,
  imports: [DatePipe, TitleCasePipe, FormsModule, TranslatePipe,
    Button, Tag, ProgressBar, InputText, InputNumber, Card, TooltipModule, Message, Select, Skeleton, DatePicker,
    Tabs, TabList, Tab, Paginator],
  templateUrl: './leave.component.html',
  styleUrl: './leave.component.scss',
})
export class LeaveComponent implements OnInit {
  readonly staffStore  = inject(StaffStore);
  private readonly leaveSvc   = inject(LeaveService);
  private readonly toast      = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);

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
  ];

  setTypeFilter(type: string): void {
    this.filterLeaveType = type;
    if (this.viewMode() === 'calendar') this.loadCalRecords();
    else this.load();
  }

  /** Overdue pending leaves (loaded separately, all pages) */
  overdueLeaves    = signal<LeaveRecord[]>([]);
  overdueExpanded  = signal(true);

  staffBalance       = signal<LeaveBalance | null>(null);
  formBalance        = signal<LeaveBalance | null>(null);   // item 4: balance shown in the add/edit form
  allBalances        = signal<StaffLeaveBalance[]>([]);
  allBalancesLoading = signal(false);
  editingEntitlement = signal(false);
  savingEntitlement  = signal(false);
  bulkSaving         = signal(false);
  carryOverSaving    = signal(false);
  entitlementForm    = { year: new Date().getFullYear(), annualHours: 112, sickHours: 112 };

  /** Item 12: Year used for the Balances overview (allows browsing prev/next years). */
  balanceYear = signal(new Date().getFullYear());


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
    this.leaveSvc.getAll({ dateFrom: from, dateTo: to, status: this.filterStatus || undefined, leaveType: this.filterLeaveType || undefined, page: 1, pageSize: 200 }).subscribe({
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
  readonly statuses: string[] = [...STATUSES];
  readonly statusOptions = STATUSES.map(s => ({
    label: s.charAt(0).toUpperCase() + s.slice(1),
    value: s,
  }));
  form: UpsertLeaveRequest = {
    employeeId: '', leaveType: 'sick', startDate: '', endDate: '',
    startTime: null, endTime: null, hours: null, notes: null, status: 'approved',
  };

  /** Separate Date bindings for the time-only pickers (UpsertLeaveRequest uses string | null). */
  startTimeDate: Date | null = null;
  endTimeDate:   Date | null = null;

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  private parseTime(t: string): Date {
    const [h, m] = t.split(':').map(Number);
    const d = new Date();
    d.setHours(h, m, 0, 0);
    return d;
  }

  /** Auto-calculated hours from the form's date+time range. */
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

  load(page = 1): void {
    this.loading.set(true);
    this.currentPage.set(page);
    this.leaveSvc.getAll({
      employeeId: this.filterEmployeeId || undefined,
      dateFrom:   this.filterFrom       || undefined,
      dateTo:     this.filterTo         || undefined,
      status:     this.filterStatus     || undefined,
      leaveType:  this.filterLeaveType  || undefined,
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

    this.loadBalances();
  }

  /** Load balance data for the Balances tab (uses balanceYear). */
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

  prevBalanceYear(): void { this.balanceYear.update(y => y - 1); this.loadBalances(); }
  nextBalanceYear(): void { this.balanceYear.update(y => y + 1); this.loadBalances(); }

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

  /** Item 5: Apply the same entitlement to all active staff. */
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

  /** Item 6: Carry over unused annual leave from previous year. */
  carryOver(): void {
    const fromYear = this.balanceYear() - 1;
    const toYear   = this.balanceYear();
    const maxHours = 14;
    this.confirmSvc.confirm({
      title:        'Carry Over Unused Leave',
      message:      `Carry over unused annual leave from ${fromYear} → ${toYear} for all active staff? Up to ${maxHours}h per person will be added to their ${toYear} annual entitlement.`,
      confirmLabel: 'Carry Over',
    }).subscribe(ok => {
      if (!ok) return;
      this.carryOverSaving.set(true);
      this.leaveSvc.carryOverEntitlement(fromYear, toYear, maxHours).subscribe({
        next: r => {
          this.carryOverSaving.set(false);
          this.toast.success(`Carry-over applied for ${r.count} staff members.`);
          this.loadBalances();
        },
        error: (e: any) => {
          this.toast.error(e?.error?.error ?? 'Carry-over failed.');
          this.carryOverSaving.set(false);
        },
      });
    });
  }

  /** Item 4: When admin selects an employee in the form, load their current balance. */
  onFormEmployeeChange(empId: string): void {
    if (!empId) { this.formBalance.set(null); return; }
    this.leaveSvc.getBalance(empId).subscribe({
      next:  b  => this.formBalance.set(b),
      error: () => this.formBalance.set(null),
    });
  }

  /** Item 10: Export current filtered records as CSV (fetches up to 1000 records). */
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
        const header = 'Staff ID,Name,Leave Type,Start Date,End Date,Hours,Status,Notes,Created At';
        const body = rows.map(r =>
          [r.staffId, r.staffName, r.leaveType, r.startDate, r.endDate,
           r.hours, r.status, r.notes ?? '', r.createdAt]
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

  quickStatus(id: string, status: 'approved' | 'rejected'): void {
    const r = this.records().find(r => r.id === id);
    if (!r) return;
    const req: UpsertLeaveRequest = {
      employeeId: r.employeeId, leaveType: r.leaveType as any,
      startDate: r.startDate, endDate: r.endDate,
      startTime: r.startTime, endTime: r.endTime, hours: r.hours,
      notes: r.notes, status,
    };
    this.leaveSvc.update(id, req).subscribe({
      next:  () => { this.load(); this.loadOverdue(); this.refreshPendingCount(); this.toast.success(`Leave ${status}.`); },
      error: () => this.toast.error('Update failed.'),
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

  countByType(type: string): number {
    return this.records()
      .filter(r => r.leaveType === type && r.status === 'approved')
      .reduce((s, r) => s + r.hours, 0);
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
      notes: r.notes, status: r.status as any };
    this.startTimeDate = r.startTime ? this.parseTime(r.startTime) : null;
    this.endTimeDate   = r.endTime   ? this.parseTime(r.endTime)   : null;
    this.editing.set(r.id);
    this.formError.set('');
    this.formBalance.set(null);
    // Load balance for the employee being edited
    this.leaveSvc.getBalance(r.employeeId).subscribe({
      next:  b  => this.formBalance.set(b),
      error: () => {},
    });
    this.showForm.set(true);
    this.scrollToForm();
  }

  cancelForm(): void { this.showForm.set(false); this.editing.set(null); this.formBalance.set(null); }

  save(): void {
    // Sync Date pickers → form string fields
    this.form.startTime = this.startTimeDate ? this.toTimeStr(this.startTimeDate) : null;
    this.form.endTime   = this.endTimeDate   ? this.toTimeStr(this.endTimeDate)   : null;

    if (!this.form.employeeId || !this.form.startDate || !this.form.endDate) {
      this.formError.set('Please fill in all required fields.'); return;
    }
    // When times are provided, validate the range; otherwise require manual hours
    if (this.form.startTime && this.form.endTime) {
      const h = this.calcHours;
      if (h <= 0) { this.formError.set('End date/time must be after start date/time.'); return; }
      this.form.hours = h;
    } else if (!this.form.hours || this.form.hours <= 0) {
      this.formError.set('Please enter start/end times or specify hours manually.'); return;
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
    this.confirmSvc.confirm({ title: 'Delete Leave Record', message: 'Remove this leave record? This cannot be undone.', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.leaveSvc.delete(id).subscribe({
          next:  () => { this.load(); this.loadOverdue(); this.refreshPendingCount(); this.toast.success('Leave deleted.'); },
          error: () => this.toast.error('Delete failed.'),
        });
      });
  }
}
