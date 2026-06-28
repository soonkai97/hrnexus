import { Component, OnInit, signal, computed, inject, DestroyRef } from '@angular/core';
import { Subject, EMPTY } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  AdminService, ReportResponse, ShiftDetailRow,
  StaffStore, StaffManagementService, ToastService, Staff,
  toLocalDateString,
  WorkLogService, UpdateWorkLogRequest, LocationStore,
  TrimInputDirective, AmPmPipe,
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
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, TrimInputDirective, AmPmPipe,
    Button, TableModule, Tag, ProgressBar, Card, TooltipModule, DatePicker, Message,
    Tabs, TabList, Tab, MultiSelect, Select, Dialog, InputText],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss',
})
export class ReportsComponent implements OnInit {
  private readonly admin              = inject(AdminService);
  private readonly staffStore         = inject(StaffStore);
  private readonly staffSvc           = inject(StaffManagementService);
  private readonly toast              = inject(ToastService);
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
  editShiftForm: UpdateWorkLogRequest = { roleName: '', locationId: '', workDate: '', startTime: '', endDate: '', endTime: '', notes: '' };
  editShiftStartTimeDate: Date | null = null; // UI binding
  editShiftEndDate:       Date | null = null; // UI binding (end date)
  editShiftEndTimeDate:   Date | null = null; // UI binding

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  private toDateStr(d: Date): string {
    return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
  }

  private parseTime(t: string): Date | null {
    if (!t) return null;
    const [h = NaN, m = NaN] = t.split(':').map(Number);
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
      endDate:    s.endDate ?? s.workDate,
      endTime:    s.endTime ?? '',
      notes:      s.notes ?? '',
    };
    this.editShiftStartTimeDate = this.parseTime(s.startTime);
    this.editShiftEndTimeDate   = this.parseTime(s.endTime ?? '');
    this.editShiftEndDate       = new Date(`${s.endDate ?? s.workDate}T00:00:00`);
    this.editShiftError.set('');
    this.editingShift.set(s);
    this.editShiftVisible.set(true);
  }

  cancelEditShift(): void {
    this.editShiftVisible.set(false);
    this.editingShift.set(null);
    this.editShiftStartTimeDate = null;
    this.editShiftEndDate       = null;
    this.editShiftEndTimeDate   = null;
  }

  saveEditShift(): void {
    const s = this.editingShift();
    if (!s) return;
    if (!this.editShiftForm.roleName || !this.editShiftForm.locationId ||
        !this.editShiftForm.workDate || !this.editShiftStartTimeDate || !this.editShiftEndDate || !this.editShiftEndTimeDate) {
      this.editShiftError.set(this.translate.instant('reports.errRequiredFields')); return;
    }
    const startTime = this.toTimeStr(this.editShiftStartTimeDate);
    const endTime   = this.toTimeStr(this.editShiftEndTimeDate);
    const endDate   = this.toDateStr(this.editShiftEndDate);
    // Duration across explicit start/end dates — a multi-day span is allowed; reject only a
    // non-positive span or an absurd one (likely a date typo).
    const durationMins = (new Date(`${endDate}T${endTime}:00`).getTime()
                        - new Date(`${this.editShiftForm.workDate}T${startTime}:00`).getTime()) / 60000;
    if (durationMins <= 0) { this.editShiftError.set(this.translate.instant('reports.errSameTime')); return; }
    if (durationMins > 14 * 1440) { this.editShiftError.set(this.translate.instant('reports.errTooLong')); return; }

    this.editShiftSaving.set(true);
    this.editShiftError.set('');
    this.workLogSvc.update(s.id, { ...this.editShiftForm, startTime, endDate, endTime, notes: this.editShiftForm.notes || null }).subscribe({
      next: () => {
        this.editShiftSaving.set(false);
        this.editShiftVisible.set(false);
        this.editingShift.set(null);
        this.editShiftStartTimeDate = null;
        this.editShiftEndDate       = null;
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
    this.lastPreset.set(preset);
    this.runReport();
  }

  /** The preset the user last clicked — preferred for the highlight so the clicked button lights up
   *  even when its range coincides with another preset's. Null until a preset is clicked (or after a
   *  manual range edit no longer matches it). */
  private readonly lastPreset = signal<'today' | 'yesterday' | 'week' | 'month' | 'last-month' | null>(null);

  /** Tie-break order for the derived (un-clicked) highlight — most specific first. Two presets can
   *  resolve to the same range (e.g. on the 2nd of a month whose 1st was a Monday, "This Week" and
   *  "This Month" both span the 1st→today); only the first match here highlights. */
  private readonly presetPriority = ['today', 'yesterday', 'week', 'month', 'last-month'] as const;

  /** The single active preset: the user's last click if its range still matches, otherwise the
   *  highest-priority preset matching the current From/To, or null for a custom range. */
  private activePreset(): 'today' | 'yesterday' | 'week' | 'month' | 'last-month' | null {
    const matching = this.presetPriority.filter(p => {
      const r = this.presetRange(p);
      return this.dateFrom === r.from && this.dateTo === r.to;
    });
    if (matching.length === 0) return null;
    const last = this.lastPreset();
    if (last && matching.includes(last)) return last;
    return matching[0] ?? null;
  }

  /** Active when this preset is the resolved active one — drives the highlight. Auto-deselects when
   *  the user picks a custom range (no preset matches). */
  isPresetActive(preset: 'today' | 'yesterday' | 'week' | 'month' | 'last-month'): boolean {
    return this.activePreset() === preset;
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
      csv  = [t('reports.csvStaffId'), t('reports.csvName'), t('reports.csvRole'), t('reports.csvLocation'), t('reports.csvClockedIn'), t('reports.csvClockedOut'), t('reports.csvHours'), t('reports.csvCost'), t('reports.csvStatus'), t('reports.shiftNotes')].join(',') + '\n';
      csv += r.shifts.map(s =>
        `${s.staffId},"${s.staffName}",${s.role},"${s.locationName}","${s.workDate} ${s.startTime}","${s.endTime ? ((s.endDate ?? s.workDate) + ' ' + s.endTime) : ''}",${s.hours},${s.cost.toFixed(2)},${s.status},"${(s.notes ?? '').replace(/"/g, '""').replace(/\r?\n/g, ' ')}"`
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

  private firstDayOfMonth(): Date {
    const d = new Date();
    d.setDate(1);
    return d;
  }

}
