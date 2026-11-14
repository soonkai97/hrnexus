import { Component, OnInit, signal, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Card } from 'primeng/card';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import {
  PayrollLockService, PayrollLockDto, ToastService, ConfirmService, apiErrorMessage,
  ViewModeService, ViewToggleComponent,
} from 'shared-lib';

@Component({
  selector: 'app-payroll-locks',
  standalone: true,
  imports: [DatePipe, FormsModule, Card, Button, TableModule, Tag, InputText, Select, Skeleton, ViewToggleComponent],
  templateUrl: './payroll-locks.component.html',
  styleUrl: './payroll-locks.component.scss',
})
export class PayrollLocksComponent implements OnInit {
  private readonly svc       = inject(PayrollLockService);
  private readonly toast     = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);

  private readonly views = inject(ViewModeService);
  /** Cards (default) or Table, remembered on this device. */
  readonly viewMode = this.views.mode('payroll-locks');

  readonly locks   = signal<PayrollLockDto[]>([]);
  readonly loading = signal(false);
  readonly saving  = signal(false);

  private readonly now = new Date();
  form = { year: this.now.getFullYear(), month: this.now.getMonth() + 1, note: '' };

  readonly monthOptions = Array.from({ length: 12 }, (_, i) => ({ value: i + 1, label: this.pad2(i + 1) }));
  // Current year back 5 years — locks are only ever created for the current or a recent period.
  readonly yearOptions = Array.from({ length: 6 }, (_, i) => {
    const y = this.now.getFullYear() - i;
    return { value: y, label: `${y}` };
  });

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (rows) => { this.locks.set(rows); this.loading.set(false); },
      error: (e: unknown) => { this.toast.error(apiErrorMessage(e) ?? 'Failed to load payroll locks.'); this.loading.set(false); },
    });
  }

  get isFormValid(): boolean {
    return this.form.year >= 2000 && this.form.year <= 2200 && this.form.month >= 1 && this.form.month <= 12;
  }

  lockPeriod(): void {
    if (!this.isFormValid || this.saving()) return;
    this.saving.set(true);
    this.svc.lock(this.form.year, this.form.month, this.form.note?.trim() || null).subscribe({
      next: () => {
        this.toast.success('Period locked.');
        this.form.note = '';
        this.saving.set(false);
        this.load();
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to lock the period.');
        this.saving.set(false);
      },
    });
  }

  reopen(lock: PayrollLockDto): void {
    this.confirmSvc.confirm({
      message: `Reopen ${this.period(lock)}? It will no longer be marked as paid out.`,
      danger: true,
    }).subscribe((ok) => {
      if (!ok) return;
      this.svc.reopen(lock.year, lock.month).subscribe({
        next: () => { this.toast.success('Period reopened.'); this.load(); },
        error: (e: unknown) => this.toast.error(apiErrorMessage(e) ?? 'Failed to reopen the period.'),
      });
    });
  }

  period(l: PayrollLockDto): string { return `${l.year}-${this.pad2(l.month)}`; }

  private pad2(n: number): string { return n < 10 ? `0${n}` : `${n}`; }
}
