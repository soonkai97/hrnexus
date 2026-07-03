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
  toLocalDateString, LocalizedNamePipe,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { DatePicker } from 'primeng/datepicker';
import { Message } from 'primeng/message';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';

/** A dimension the grouped pivot can group shift rows by. */
export type GroupDim = 'staff' | 'location' | 'job' | 'date';

/** One displayed grouping cell (carries localized variants so the label can be translated). */
interface GroupCell { dim: GroupDim; text: string; zh: string | null; vi: string | null; }

/** One aggregated pivot row: the grouping cells plus the rolled-up measures. */
export interface GroupedRow {
  cells:    GroupCell[];
  /** dim → cell text, used as the p-table sort field path (`sortKeys.staff`, …). */
  sortKeys: Record<string, string>;
  shifts:   number;
  hours:    number;
  avg:      number;
  cost:     number;
}

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, LocalizedNamePipe,
    Button, TableModule, Tag, Card, TooltipModule, DatePicker, Message,
    Tabs, TabList, Tab, MultiSelect, Select],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss',
})
export class ReportsComponent implements OnInit {
  private readonly admin              = inject(AdminService);
  private readonly staffStore         = inject(StaffStore);
  private readonly staffSvc           = inject(StaffManagementService);
  private readonly toast              = inject(ToastService);
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

  activeTab = signal<'grouped' | 'customer'>('grouped');

  readonly tabOptions = [
    { label: 'reports.grouped',     value: 'grouped'  },
    { label: 'reports.customerLog', value: 'customer' },
  ];

  // ── Grouped pivot ─────────────────────────────────────────────────────────────
  // The old By-Staff / By-Location / By-Job / By-Date tabs were four frozen views of the
  // same per-shift data. They are now one dynamic pivot: pick any dimension(s) to group by
  // and the aggregation is computed client-side from report().shifts — no backend call.
  readonly groupDimOptions: { label: string; value: GroupDim }[] = [
    { label: 'reports.groupStaff',    value: 'staff'    },
    { label: 'reports.groupLocation', value: 'location' },
    { label: 'reports.groupJob',      value: 'job'      },
    { label: 'reports.groupDate',     value: 'date'     },
  ];

  private readonly dimLabels: Record<GroupDim, string> = {
    staff: 'reports.groupStaff', location: 'reports.groupLocation', job: 'reports.groupJob', date: 'reports.groupDate',
  };
  dimLabelKey(dim: GroupDim): string { return this.dimLabels[dim]; }

  groupBy = signal<GroupDim[]>(['staff']);

  /** Never allow an empty grouping — fall back to Staff so the pivot always has ≥1 dimension. */
  onGroupByChange(dims: GroupDim[]): void {
    this.groupBy.set(dims && dims.length ? dims : ['staff']);
  }

  /** Translated "Staff + Job" style label for the multiselect's collapsed display. */
  groupByLabel(): string {
    return this.groupBy().map(d => this.translate.instant(this.dimLabelKey(d))).join(' + ');
  }

  /** The pivot rows — recomputed whenever the report or the chosen dimensions change. */
  readonly groupedRows = computed<GroupedRow[]>(() =>
    this.buildGroups(this.report()?.shifts ?? [], this.groupBy()),
  );

  private dimKey(dim: GroupDim, s: ShiftDetailRow): string {
    switch (dim) {
      case 'staff':    return s.staffId;
      case 'location': return s.locationId;
      case 'job':      return s.role;
      case 'date':     return s.workDate;
    }
  }

  private dimCell(dim: GroupDim, s: ShiftDetailRow): GroupCell {
    switch (dim) {
      case 'staff':    return { dim, text: s.staffName,    zh: null,                   vi: null };
      case 'location': return { dim, text: s.locationName, zh: s.locationNameZh ?? null, vi: s.locationNameVi ?? null };
      case 'job':      return { dim, text: s.role,         zh: s.roleZh ?? null,         vi: s.roleVi ?? null };
      case 'date':     return { dim, text: s.workDate,     zh: null,                   vi: null };
    }
  }

  /** Group the raw shift rows by the chosen dimension(s) and roll up shifts/hours/cost/avg. */
  private buildGroups(shifts: ShiftDetailRow[], dims: GroupDim[]): GroupedRow[] {
    const ds = dims.length ? dims : (['staff'] as GroupDim[]);
    const map = new Map<string, GroupedRow>();
    for (const s of shifts) {
      const key = ds.map(d => this.dimKey(d, s)).join('␟');
      let row = map.get(key);
      if (!row) {
        const cells = ds.map(d => this.dimCell(d, s));
        const sortKeys: Record<string, string> = {};
        for (const c of cells) sortKeys[c.dim] = c.text ?? '';
        row = { cells, sortKeys, shifts: 0, hours: 0, avg: 0, cost: 0 };
        map.set(key, row);
      }
      row.shifts += 1;
      row.hours  += s.hours;
      row.cost   += s.cost;
    }
    const rows = [...map.values()];
    for (const r of rows) {
      r.avg   = r.shifts ? r.hours / r.shifts : 0;
      r.hours = Math.round(r.hours * 100) / 100;
      r.cost  = Math.round(r.cost  * 100) / 100;
    }
    return rows;
  }

  get datePresets() {
    return [
      { label: this.translate.instant('reports.today'),      value: 'today'      as const },
      { label: this.translate.instant('reports.yesterday'),  value: 'yesterday'  as const },
      { label: this.translate.instant('reports.thisWeek'),   value: 'week'       as const },
      { label: this.translate.instant('reports.thisMonth'),  value: 'month'      as const },
      { label: this.translate.instant('reports.lastMonth'),  value: 'last-month' as const },
    ];
  }

  ngOnInit(): void {
    this.staffSvc.getAll('', 1, 999).subscribe({
      next: r => this.allStaff.set(r.items),
      error: () => {},
    });
    this.staffStore.loadRoles();
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
    const q = (v: string) => `"${(v ?? '').replace(/"/g, '""').replace(/\r?\n/g, ' ')}"`;
    let csv = '';
    if (tab === 'grouped') {
      // Dynamic layout: one column per chosen dimension, then the rolled-up measures.
      // Dimension cells use the canonical English snapshot text (not the localized label).
      const dims = this.groupBy();
      csv  = [...dims.map(d => t(this.dimLabelKey(d))), t('reports.csvShifts'), t('reports.csvHours'), t('reports.csvAvgHShift'), t('reports.csvGrossPay')].join(',') + '\n';
      csv += this.groupedRows().map(row =>
        [...row.cells.map(c => q(c.text)), row.shifts, row.hours, row.avg.toFixed(1), row.cost.toFixed(2)].join(',')
      ).join('\n');
    } else {
      // Customer log (the per-shift tab): staff name, working date, customer (the shift's notes
      // field), location, job, total hours, total rate. Matches the on-screen columns; English
      // snapshot names in the CSV (canonical).
      csv  = [t('reports.name'), t('reports.workingDate'), t('reports.customer'), t('reports.csvLocation'), t('reports.csvRole'), t('reports.totalHour'), t('reports.totalRate')].join(',') + '\n';
      csv += r.shifts.map(s =>
        `${q(s.staffName)},${s.workDate},${q(s.notes ?? '')},${q(s.locationName)},${s.role},${s.hours},${s.cost.toFixed(2)}`
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
