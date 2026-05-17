import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import {
  AdminService, ReportResponse, ReportFilters,
  LocationReportRow, StaffStore, StaffService, ToastService, Staff,
  toLocalDateString,
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

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [DatePipe, DecimalPipe, TitleCasePipe, FormsModule, TranslatePipe,
    Button, TableModule, Tag, ProgressBar, Card, TooltipModule, DatePicker, Message, Tabs, TabList, Tab, MultiSelect],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss',
})
export class ReportsComponent implements OnInit {
  private readonly admin      = inject(AdminService);
  private readonly staffStore = inject(StaffStore);
  private readonly staffSvc   = inject(StaffService);
  private readonly toast      = inject(ToastService);

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
    this.staffSvc.getLocations().subscribe({
      next: locs => this.locations.set(locs.map(l => l.name)),
    });
    this.runReport();
  }

  runReport(): void {
    this.loading.set(true);
    this.error.set('');

    // Guard: backend enforces 366-day cap — catch it here for immediate feedback
    const from = new Date(this.dateFrom);
    const to   = new Date(this.dateTo);
    const rangeDays = (to.getTime() - from.getTime()) / (1000 * 60 * 60 * 24);
    if (rangeDays > 366) {
      this.toast.error('Date range cannot exceed 366 days. Please narrow your filter.');
      this.loading.set(false);
      return;
    }

    // Normalise to [] when PrimeNG clear (×) button sets the value to null.
    // Treat "all options selected" as "no filter" — avoids blasting every ID
    // into the query string when the user clicks Select All.
    const staffIds      = this.staffIds      ?? [];
    const locationNames = this.locationNames ?? [];
    const roles         = this.roles         ?? [];

    const filters: ReportFilters = {
      dateFrom:      this.dateFrom,
      dateTo:        this.dateTo,
      staffIds:      staffIds.length      && staffIds.length      < this.allStaff().length   ? staffIds      : undefined,
      locationNames: locationNames.length && locationNames.length < this.locations().length  ? locationNames : undefined,
      roles:         roles.length         && roles.length         < this.staffRoles().length ? roles         : undefined,
    };
    this.admin.getReport(filters).subscribe({
      next:  data  => { this.report.set(data); this.loading.set(false); },
      error: ()    => { this.error.set('Failed to load report.'); this.toast.error('Failed to load report.'); this.loading.set(false); },
    });
  }

  setPreset(preset: 'today' | 'yesterday' | 'week' | 'month' | 'last-month'): void {
    const now = new Date();
    const fmt = (d: Date) => toLocalDateString(d);
    switch (preset) {
      case 'today':
        this.dateFrom = this.dateTo = fmt(now); break;
      case 'yesterday': {
        const y = new Date(now); y.setDate(now.getDate() - 1);
        this.dateFrom = this.dateTo = fmt(y); break;
      }
      case 'week': {
        const mon = new Date(now);
        mon.setDate(now.getDate() - ((now.getDay() + 6) % 7)); // Monday
        this.dateFrom = fmt(mon); this.dateTo = fmt(now); break;
      }
      case 'month':
        this.dateFrom = fmt(this.firstDayOfMonth()); this.dateTo = fmt(now); break;
      case 'last-month': {
        const first = new Date(now.getFullYear(), now.getMonth() - 1, 1);
        const last  = new Date(now.getFullYear(), now.getMonth(), 0);
        this.dateFrom = fmt(first); this.dateTo = fmt(last); break;
      }
    }
    this.runReport();
  }

  // ── Workforce Report CSV export ─────────────────────────────────────────────
  reportExporting = signal(false);

  downloadReportCsv(): void {
    const r = this.report();
    if (!r) return;
    this.reportExporting.set(true);
    const tab = this.activeTab();

    let csv = '';
    if (tab === 'employee') {
      csv  = 'Staff ID,Name,Role,Shifts,Hours,Avg h/shift,Gross Pay\n';
      csv += r.byStaff.map(e =>
        `${e.staffId},"${e.name}",${e.role},${e.shifts},${e.hours},${e.avgHoursPerShift},${e.cost.toFixed(2)}`
      ).join('\n');
    } else if (tab === 'location') {
      csv  = 'Location,Shifts,Hours,Labour Cost\n';
      csv += r.byLocation.map(l =>
        `"${l.locationName}",${l.shifts},${l.hours},${l.cost.toFixed(2)}`
      ).join('\n');
    } else if (tab === 'role') {
      csv  = 'Role,Staff Count,Shifts,Hours,Labour Cost\n';
      csv += r.byRole.map(ro =>
        `${ro.role},${ro.staffCount},${ro.shifts},${ro.hours},${ro.cost.toFixed(2)}`
      ).join('\n');
    } else if (tab === 'date') {
      csv  = 'Date,Shifts,Hours,Labour Cost\n';
      csv += r.byDate.map(d =>
        `${d.date},${d.shifts},${d.hours},${d.cost.toFixed(2)}`
      ).join('\n');
    } else {
      csv  = 'Staff ID,Name,Role,Location,Clocked In,Clocked Out,Hours,Cost,Status\n';
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
    this.toast.success('Report downloaded.');
  }

  private firstDayOfMonth(): Date {
    const d = new Date();
    d.setDate(1);
    return d;
  }

}
