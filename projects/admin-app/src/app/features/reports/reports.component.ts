import { Component, OnInit, signal, computed, inject, DestroyRef } from '@angular/core';
import { Subject, EMPTY } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  AdminService, ReportResponse, ReportFilters, ShiftDetailRow,
  LocationReportRow, StaffStore, StaffManagementService, ToastService, Staff,
  toLocalDateString,
  StaffService, FeatureFlagService,
  StaffLedgerPayoutDto, StaffOutstandingDto,
  WORK_TYPE_LABELS, SETTLEMENT_LABELS,
  WorkLogService, UpdateWorkLogRequest, LocationStore,
  TrimInputDirective,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { ProgressBar } from 'primeng/progressbar';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { DatePicker } from 'primeng/datepicker';
import { Message } from 'primeng/message';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, TranslatePipe, TrimInputDirective,
    Button, TableModule, Tag, ProgressBar, Card, TooltipModule, DatePicker, Message,
    Tabs, TabList, Tab, MultiSelect, Select, Skeleton, Dialog, InputText],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss',
})
export class ReportsComponent implements OnInit {
  private readonly admin              = inject(AdminService);
  private readonly staffStore         = inject(StaffStore);
  private readonly staffSvc           = inject(StaffManagementService);
  private readonly toast              = inject(ToastService);
  private readonly staffLedgerSvc     = inject(StaffService);
  private readonly featureFlagSvc     = inject(FeatureFlagService);
  private readonly workLogSvc         = inject(WorkLogService);
  private readonly locationStore      = inject(LocationStore);
  private readonly translate          = inject(TranslateService);
  private readonly destroyRef         = inject(DestroyRef);

  // switchMap cancels any in-flight getReport request when a newer runReport() call arrives.
  private readonly _reportTrigger$ = new Subject<void>();

  constructor() {
    this._reportTrigger$.pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set('');

        const from = new Date(this.dateFrom);
        const to   = new Date(this.dateTo);
        if ((to.getTime() - from.getTime()) / (1000 * 60 * 60 * 24) > 366) {
          this.toast.error(this.translate.instant('reports.errDateRange'));
          this.loading.set(false);
          return EMPTY;
        }

        const staffIds      = this.staffIds      ?? [];
        const locationNames = this.locationNames ?? [];
        const roles         = this.roles         ?? [];

        return this.admin.getReport({
          dateFrom:      this.dateFrom,
          dateTo:        this.dateTo,
          staffIds:      staffIds.length      && staffIds.length      < this.allStaff().length   ? staffIds      : undefined,
          locationNames: locationNames.length && locationNames.length < this.locations().length  ? locationNames : undefined,
          roles:         roles.length         && roles.length         < this.staffRoles().length ? roles         : undefined,
        }).pipe(
          catchError(() => {
            this.error.set(this.translate.instant('reports.loadFailed'));
            this.toast.error(this.translate.instant('reports.loadFailed'));
            this.loading.set(false);
            return EMPTY;
          }),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(data => {
      this.report.set(data);
      this.loading.set(false);
    });
  }

  // ── Section switcher ─────────────────────────────────────────────────────────

  activeSection = signal<'workforce' | 'ledger'>('workforce');
  isStaffLedgerEnabled = computed(() => this.featureFlagSvc.isEnabled('staffLedger'));

  // ── Workforce Report ─────────────────────────────────────────────────────────

  // Load all staff independently (not via paginated store) so the dropdown
  // shows every staff member, not just the first page of 10.
  allStaff            = signal<Staff[]>([]);
  readonly staffRoles = this.staffStore.roles;
  loading    = signal(false);
  error      = signal('');
  report     = signal<ReportResponse | null>(null);

  locations  = signal<string[]>([]);

  // Filters
  dateFrom      = toLocalDateString(this.firstDayOfMonth());
  dateTo        = toLocalDateString(new Date());
  staffIds:      string[] = [];
  locationNames: string[] = [];
  roles:         string[] = [];

  activeTab = signal<'employee' | 'location' | 'role' | 'date' | 'shifts'>('employee');

  readonly tabOptions = [
    { label: 'reports.byEmployee', value: 'employee' },
    { label: 'reports.byLocation', value: 'location' },
    { label: 'reports.byRole',     value: 'role'     },
    { label: 'reports.byDate',     value: 'date'     },
    { label: 'reports.allShifts',  value: 'shifts'   },
  ];

  get datePresets() {
    return [
      { label: this.translate.instant('reports.today'),      value: 'today'      as const },
      { label: this.translate.instant('reports.yesterday'),  value: 'yesterday'  as const },
      { label: this.translate.instant('reports.thisWeek'),   value: 'week'       as const },
      { label: this.translate.instant('reports.thisMonth'),  value: 'month'      as const },
      { label: this.translate.instant('reports.lastMonth'),  value: 'last-month' as const },
    ];
  }

  // ── Edit shift (manager can edit any staff's shift) ───────────────────────────
  readonly locationOptions = computed(() =>
    this.locationStore.locations().map(l => ({ label: l.name, value: l.id }))
  );
  readonly roleOptions = computed(() =>
    this.staffStore.roles().map(r => ({ label: r.name.toUpperCase(), value: r.name }))
  );

  editShiftVisible = signal(false);
  editShiftSaving  = signal(false);
  editShiftError   = signal('');
  editingShift     = signal<ShiftDetailRow | null>(null);
  editShiftForm: UpdateWorkLogRequest = { roleName: '', locationId: '', workDate: '', startTime: '', endTime: '', notes: '' };
  editShiftStartTimeDate: Date | null = null; // UI binding
  editShiftEndTimeDate:   Date | null = null; // UI binding

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  private parseTime(t: string): Date | null {
    if (!t) return null;
    const [h, m] = t.split(':').map(Number);
    if (isNaN(h) || isNaN(m)) return null;
    const d = new Date();
    d.setHours(h, m, 0, 0);
    return d;
  }

  openEditShift(s: ShiftDetailRow): void {
    this.editShiftForm = {
      roleName:   s.role,
      locationId: s.locationId,
      workDate:   s.workDate,
      startTime:  s.startTime,
      endTime:    s.endTime ?? '',
      notes:      s.notes ?? '',
    };
    this.editShiftStartTimeDate = this.parseTime(s.startTime);
    this.editShiftEndTimeDate   = this.parseTime(s.endTime ?? '');
    this.editShiftError.set('');
    this.editingShift.set(s);
    this.editShiftVisible.set(true);
  }

  cancelEditShift(): void {
    this.editShiftVisible.set(false);
    this.editingShift.set(null);
    this.editShiftStartTimeDate = null;
    this.editShiftEndTimeDate   = null;
  }

  saveEditShift(): void {
    const s = this.editingShift();
    if (!s) return;
    if (!this.editShiftForm.roleName || !this.editShiftForm.locationId ||
        !this.editShiftForm.workDate || !this.editShiftStartTimeDate || !this.editShiftEndTimeDate) {
      this.editShiftError.set(this.translate.instant('reports.errRequiredFields')); return;
    }
    const startTime = this.toTimeStr(this.editShiftStartTimeDate);
    const endTime   = this.toTimeStr(this.editShiftEndTimeDate);
    const [sh, sm] = startTime.split(':').map(Number);
    const [eh, em] = endTime.split(':').map(Number);
    const durationMins = (eh * 60 + em) >= (sh * 60 + sm)
      ? (eh * 60 + em) - (sh * 60 + sm)
      : (eh * 60 + em) + 1440 - (sh * 60 + sm);
    if (durationMins === 0) { this.editShiftError.set(this.translate.instant('reports.errSameTime')); return; }
    if (durationMins > 1440) { this.editShiftError.set(this.translate.instant('reports.errOver24h')); return; }

    this.editShiftSaving.set(true);
    this.editShiftError.set('');
    this.workLogSvc.update(s.id, { ...this.editShiftForm, startTime, endTime, notes: this.editShiftForm.notes || null }).subscribe({
      next: () => {
        this.editShiftSaving.set(false);
        this.editShiftVisible.set(false);
        this.editingShift.set(null);
        this.editShiftStartTimeDate = null;
        this.editShiftEndTimeDate   = null;
        this.toast.success(this.translate.instant('reports.shiftUpdated'));
        this.runReport();
      },
      error: (e) => {
        this.editShiftError.set(e?.error?.error ?? this.translate.instant('reports.shiftUpdateFailed'));
        this.editShiftSaving.set(false);
      },
    });
  }

  maxLocationCost = computed(() => {
    const rows = this.report()?.byLocation ?? [];
    return rows.length ? Math.max(...rows.map(r => r.cost)) : 1;
  });

  ngOnInit(): void {
    this.staffSvc.getAll('', 1, 999).subscribe({
      next: r => this.allStaff.set(r.items),
      error: () => {},
    });
    this.staffStore.loadRoles();
    this.locationStore.load();
    this.staffSvc.getLocations().subscribe({
      next: locs => this.locations.set(locs.map(l => l.name)),
    });
    this.runReport();
  }

  runReport(): void {
    // switchMap in constructor cancels any in-flight request when a newer call arrives
    this._reportTrigger$.next();
  }

  /** The From/To range a preset produces, computed against today. */
  private presetRange(preset: 'today' | 'yesterday' | 'week' | 'month' | 'last-month'): { from: string; to: string } {
    const now = new Date();
    const fmt = (d: Date) => toLocalDateString(d);
    switch (preset) {
      case 'today': return { from: fmt(now), to: fmt(now) };
      case 'yesterday': {
        const y = new Date(now); y.setDate(now.getDate() - 1);
        return { from: fmt(y), to: fmt(y) };
      }
      case 'week': {
        const mon = new Date(now);
        mon.setDate(now.getDate() - ((now.getDay() + 6) % 7)); // Monday
        return { from: fmt(mon), to: fmt(now) };
      }
      case 'month':
        return { from: fmt(this.firstDayOfMonth()), to: fmt(now) };
      case 'last-month': {
        const first = new Date(now.getFullYear(), now.getMonth() - 1, 1);
        const last  = new Date(now.getFullYear(), now.getMonth(), 0);
        return { from: fmt(first), to: fmt(last) };
      }
    }
  }

  setPreset(preset: 'today' | 'yesterday' | 'week' | 'month' | 'last-month'): void {
    const r = this.presetRange(preset);
    this.dateFrom = r.from;
    this.dateTo   = r.to;
    this.runReport();
  }

  /** Active when the current From/To exactly matches this preset — drives the highlight.
   *  Auto-deselects when the user picks a custom range (no preset will match). */
  isPresetActive(preset: 'today' | 'yesterday' | 'week' | 'month' | 'last-month'): boolean {
    const r = this.presetRange(preset);
    return this.dateFrom === r.from && this.dateTo === r.to;
  }

  // ── Workforce Report CSV export ─────────────────────────────────────────────
  reportExporting = signal(false);

  downloadReportCsv(): void {
    const r = this.report();
    if (!r) return;
    this.reportExporting.set(true);
    const tab = this.activeTab();

    const t = (k: string) => this.translate.instant(k);
    let csv = '';
    if (tab === 'employee') {
      csv  = [t('reports.csvStaffId'), t('reports.csvName'), t('reports.csvRole'), t('reports.csvShifts'), t('reports.csvHours'), t('reports.csvAvgHShift'), t('reports.csvGrossPay')].join(',') + '\n';
      csv += r.byStaff.map(e =>
        `${e.staffId},"${e.name}",${e.role},${e.shifts},${e.hours},${e.avgHoursPerShift},${e.cost.toFixed(2)}`
      ).join('\n');
    } else if (tab === 'location') {
      csv  = [t('reports.csvLocation'), t('reports.csvShifts'), t('reports.csvHours'), t('reports.csvLabourCost')].join(',') + '\n';
      csv += r.byLocation.map(l =>
        `"${l.locationName}",${l.shifts},${l.hours},${l.cost.toFixed(2)}`
      ).join('\n');
    } else if (tab === 'role') {
      csv  = [t('reports.csvRole'), t('reports.csvStaffCount'), t('reports.csvShifts'), t('reports.csvHours'), t('reports.csvLabourCost')].join(',') + '\n';
      csv += r.byRole.map(ro =>
        `${ro.role},${ro.staffCount},${ro.shifts},${ro.hours},${ro.cost.toFixed(2)}`
      ).join('\n');
    } else if (tab === 'date') {
      csv  = [t('reports.csvDate'), t('reports.csvShifts'), t('reports.csvHours'), t('reports.csvLabourCost')].join(',') + '\n';
      csv += r.byDate.map(d =>
        `${d.date},${d.shifts},${d.hours},${d.cost.toFixed(2)}`
      ).join('\n');
    } else {
      csv  = [t('reports.csvStaffId'), t('reports.csvName'), t('reports.csvRole'), t('reports.csvLocation'), t('reports.csvClockedIn'), t('reports.csvClockedOut'), t('reports.csvHours'), t('reports.csvCost'), t('reports.csvStatus')].join(',') + '\n';
      csv += r.shifts.map(s =>
        `${s.staffId},"${s.staffName}",${s.role},"${s.locationName}",${s.clockedIn},${s.clockedOut ?? ''},${s.hours},${s.cost.toFixed(2)},${s.status}`
      ).join('\n');
    }

    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = `workforce-report-${tab}-${this.dateFrom}-to-${this.dateTo}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    this.reportExporting.set(false);
    this.toast.success(this.translate.instant('reports.reportDownloaded'));
  }

  // ── Staff Ledger Payout ───────────────────────────────────────────────────────

  get MONTH_OPTS() {
    return [
      { label: this.translate.instant('reports.month1'),  value: 1  },
      { label: this.translate.instant('reports.month2'),  value: 2  },
      { label: this.translate.instant('reports.month3'),  value: 3  },
      { label: this.translate.instant('reports.month4'),  value: 4  },
      { label: this.translate.instant('reports.month5'),  value: 5  },
      { label: this.translate.instant('reports.month6'),  value: 6  },
      { label: this.translate.instant('reports.month7'),  value: 7  },
      { label: this.translate.instant('reports.month8'),  value: 8  },
      { label: this.translate.instant('reports.month9'),  value: 9  },
      { label: this.translate.instant('reports.month10'), value: 10 },
      { label: this.translate.instant('reports.month11'), value: 11 },
      { label: this.translate.instant('reports.month12'), value: 12 },
    ];
  }

  readonly yearOpts = [-2, -1, 0, 1].map(offset => {
    const y = new Date().getFullYear() + offset;
    return { label: String(y), value: y };
  });

  ledgerYear    = signal(new Date().getFullYear());
  ledgerMonth   = signal(new Date().getMonth() + 1);
  ledgerPayout  = signal<StaffLedgerPayoutDto[]>([]);
  ledgerOutstanding = signal<StaffOutstandingDto[]>([]);
  ledgerLoading = signal(false);
  ledgerExporting = signal(false);

  ledgerTotalVenue   = computed(() => this.ledgerPayout().reduce((s, r) => s + r.venueEarnings, 0));
  ledgerTotalPrivate = computed(() => this.ledgerPayout().reduce((s, r) => s + r.privateNet, 0));
  ledgerTotalPayout  = computed(() => this.ledgerPayout().reduce((s, r) => s + r.netPayout, 0));

  loadLedgerData(): void {
    if (this.ledgerLoading()) return; // dedup: skip if a fetch is already in flight
    this.ledgerLoading.set(true);
    this.staffLedgerSvc.summary(this.ledgerYear(), this.ledgerMonth()).subscribe({
      next:  rows => { this.ledgerPayout.set(rows); this.ledgerLoading.set(false); },
      error: ()   => { this.toast.error(this.translate.instant('reports.ledgerLoadFailed')); this.ledgerLoading.set(false); },
    });
    this.staffLedgerSvc.outstanding().subscribe({
      next:  rows => this.ledgerOutstanding.set(rows),
      error: ()   => {},
    });
  }

  workTypeLabel(key: string): string {
    return (WORK_TYPE_LABELS as Record<string, string>)[key] ?? key;
  }

  settlementLabel(key: string): string {
    return (SETTLEMENT_LABELS as Record<string, string>)[key] ?? key;
  }

  // ── Outstanding bulk-settle ───────────────────────────────────────────────────

  /** Settlement options for the bulk-settle dropdown — only the "resolved" states. */
  get SETTLE_OPTIONS() {
    return [
      { label: this.translate.instant('reports.settleSettled'), value: 'SETTLED' },
      { label: this.translate.instant('reports.settleCash'),    value: 'CASH'    },
      { label: this.translate.instant('reports.settleBankIn'),  value: 'BANKIN'  },
    ];
  }

  /** Rows selected in the outstanding table (PrimeNG two-way binding target). */
  outstandingSelection: StaffOutstandingDto[] = [];
  settleWith  = signal('SETTLED');
  settling    = signal(false);

  bulkSettle(): void {
    const selected = this.outstandingSelection;
    if (!selected.length) return;
    this.settling.set(true);
    const entryIds = selected.map(e => e.entryId);
    this.staffLedgerSvc.bulkSettle(entryIds, this.settleWith()).subscribe({
      next: (res) => {
        const n = res.updated;
        this.toast.success(this.translate.instant(n === 1 ? 'reports.settledSingular' : 'reports.settledPlural', { n }));
        this.outstandingSelection = [];
        this.loadLedgerData();
        this.settling.set(false);
      },
      error: () => {
        this.toast.error(this.translate.instant('reports.settleFailed'));
        this.settling.set(false);
      },
    });
  }

  downloadLedgerCsv(): void {
    const rows = this.ledgerPayout();
    if (!rows.length) return;
    this.ledgerExporting.set(true);

    const monthStr = String(this.ledgerMonth()).padStart(2, '0');
    const t = (k: string) => this.translate.instant(k);
    let csv = [t('reports.csvStaffCode'), t('reports.csvName'), t('reports.ledgerVenueCol'), t('reports.ledgerPrivateCol'), t('reports.ledgerPayoutCol'), t('reports.ledgerEntriesCol'), t('reports.ledgerPendingCol'), t('reports.ledgerNotCollectedCol')].join(',') + '\n';
    csv += rows.map(r =>
      `${r.staffCode},"${r.staffName}",${r.venueEarnings.toFixed(2)},${r.privateNet.toFixed(2)},${r.netPayout.toFixed(2)},${r.entryCount},${r.pendingCount},${r.notCollectedCount}`
    ).join('\n');

    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = `staff-ledger-payout-${this.ledgerYear()}-${monthStr}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    this.ledgerExporting.set(false);
    this.toast.success(this.translate.instant('reports.payoutDownloaded'));
  }

  private firstDayOfMonth(): Date {
    const d = new Date();
    d.setDate(1);
    return d;
  }

}
