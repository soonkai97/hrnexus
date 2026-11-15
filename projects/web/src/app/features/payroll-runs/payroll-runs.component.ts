import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { InputNumber } from 'primeng/inputnumber';
import { PrimeTemplate } from 'primeng/api';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import {
  PayrollRunService,
  PayrollRunSummary,
  PermissionService,
  ToastService,
  ViewModeService,
  ViewToggleComponent,
  apiErrorMessage,
  monthLabel,
  rm,
  runStateTag,
} from 'shared-lib';

const MONTH_NAMES = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

/**
 * Every payroll run, newest month first, with its state, headcount, sign-off progress and totals (the list or cards). Anyone
 * with payroll.view sees it; payroll.manage can start a month. The server enforces both; the button is hidden as a
 * convenience. Money is only formatted here, never calculated.
 */
@Component({
  selector: 'app-payroll-runs',
  standalone: true,
  imports: [
    FormsModule,
    Button,
    Card,
    Dialog,
    InputNumber,
    PrimeTemplate,
    Select,
    Skeleton,
    TableModule,
    Tag,
    ViewToggleComponent,
  ],
  templateUrl: './payroll-runs.component.html',
  styleUrl: './payroll-runs.component.scss',
})
export class PayrollRunsComponent implements OnInit {
  private readonly svc = inject(PayrollRunService);
  private readonly perms = inject(PermissionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly views = inject(ViewModeService);

  /** The list (table) by default, cards on request; remembered on this device. */
  readonly viewMode = this.views.mode('payroll-runs');

  readonly runs = signal<PayrollRunSummary[]>([]);
  readonly loading = signal(false);
  readonly empty = computed(() => !this.loading() && this.runs().length === 0);

  readonly monthLabel = monthLabel;
  readonly rm = rm;
  readonly tag = runStateTag;

  get canStart(): boolean {
    return this.perms.has('payroll.manage');
  }

  // ── Start a month ────────────────────────────────────────────────────────────

  readonly showStart = signal(false);
  readonly starting = signal(false);
  readonly monthOptions = MONTH_NAMES.map((label, i) => ({ label, value: i + 1 }));
  startMonth = 1;
  startYear = 2026;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (rows) => {
        this.runs.set(rows);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load payroll runs.');
        this.loading.set(false);
      },
    });
  }

  progress(r: PayrollRunSummary): string {
    return r.state === 'draft' ? '—' : `${r.signedOff} of ${r.people} signed`;
  }

  open(r: PayrollRunSummary): void {
    void this.router.navigate(['/payroll-runs', r.id]);
  }

  /** Opens the start dialog on last month, which is the month most people are paying. */
  openStart(): void {
    const now = new Date();
    const last = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    this.startYear = last.getFullYear();
    this.startMonth = last.getMonth() + 1;
    this.showStart.set(true);
  }

  get isStartValid(): boolean {
    return (
      this.startYear >= 2000 &&
      this.startYear <= 2200 &&
      this.startMonth >= 1 &&
      this.startMonth <= 12
    );
  }

  start(): void {
    if (!this.isStartValid || this.starting()) return;
    this.starting.set(true);
    this.svc.start(this.startYear, this.startMonth).subscribe({
      next: ({ id }) => {
        this.starting.set(false);
        this.showStart.set(false);
        void this.router.navigate(['/payroll-runs', id]);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to start the payroll run.');
        this.starting.set(false);
      },
    });
  }
}
