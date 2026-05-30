import { Component, OnInit, signal, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Card } from 'primeng/card';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import {
  PayrollLockService, PayrollLockDto, ToastService, ConfirmService, apiErrorMessage,
} from 'shared-lib';

@Component({
  selector: 'app-payroll-locks',
  standalone: true,
  imports: [DatePipe, FormsModule, TranslatePipe, Card, Button, TableModule, Tag, InputText, InputNumber, Select, Skeleton],
  templateUrl: './payroll-locks.component.html',
  styleUrl: './payroll-locks.component.scss',
})
export class PayrollLocksComponent implements OnInit {
  private readonly svc       = inject(PayrollLockService);
  private readonly toast     = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly translate = inject(TranslateService);

  readonly locks   = signal<PayrollLockDto[]>([]);
  readonly loading = signal(false);
  readonly saving  = signal(false);

  private readonly now = new Date();
  form = { year: this.now.getFullYear(), month: this.now.getMonth() + 1, note: '' };

  readonly monthOptions = Array.from({ length: 12 }, (_, i) => ({ value: i + 1, label: this.pad2(i + 1) }));

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (rows) => { this.locks.set(rows); this.loading.set(false); },
      error: (e: unknown) => { this.toast.error(apiErrorMessage(e) ?? this.translate.instant('payrollLocks.loadFailed')); this.loading.set(false); },
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
        this.toast.success(this.translate.instant('payrollLocks.lockedToast'));
        this.form.note = '';
        this.saving.set(false);
        this.load();
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? this.translate.instant('payrollLocks.lockFailed'));
        this.saving.set(false);
      },
    });
  }

  reopen(lock: PayrollLockDto): void {
    this.confirmSvc.confirm({
      message: this.translate.instant('payrollLocks.reopenConfirm', { period: this.period(lock) }),
      danger: true,
    }).subscribe((ok) => {
      if (!ok) return;
      this.svc.reopen(lock.year, lock.month).subscribe({
        next: () => { this.toast.success(this.translate.instant('payrollLocks.reopenedToast')); this.load(); },
        error: (e: unknown) => this.toast.error(apiErrorMessage(e) ?? this.translate.instant('payrollLocks.reopenFailed')),
      });
    });
  }

  period(l: PayrollLockDto): string { return `${l.year}-${this.pad2(l.month)}`; }

  private pad2(n: number): string { return n < 10 ? `0${n}` : `${n}`; }
}
