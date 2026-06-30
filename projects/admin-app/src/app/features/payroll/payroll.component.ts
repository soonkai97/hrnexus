import { Component, signal, computed, inject, viewChild, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, PayrollSummaryResponse, EarningsSnapshotResponse,
         PayrollStaffRow, ToastService } from 'shared-lib';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { Message } from 'primeng/message';
import { PayslipDialogComponent } from '../../shared/payslip-dialog/payslip-dialog.component';

@Component({
  selector: 'app-payroll',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, Button, TableModule, Card, Select, Message,
    PayslipDialogComponent],
  templateUrl: './payroll.component.html',
  styleUrl: './payroll.component.scss',
})
export class PayrollComponent implements OnInit {
  private readonly adminSvc   = inject(AdminService);
  private readonly toast      = inject(ToastService);
  private readonly translate  = inject(TranslateService);

  year      = signal(new Date().getFullYear());
  month     = signal(new Date().getMonth() + 1);
  loading   = signal(false);
  exporting = signal(false);
  data      = signal<PayrollSummaryResponse | null>(null);

  // Live "earned today / this month" snapshot — loaded on entry, independent of the month picker.
  snapshot        = signal<EarningsSnapshotResponse | null>(null);
  snapshotLoading = signal(false);

  ngOnInit(): void { this.loadSnapshot(); }

  loadSnapshot(): void {
    this.snapshotLoading.set(true);
    this.adminSvc.getEarningsSnapshot().subscribe({
      next: s => { this.snapshot.set(s); this.snapshotLoading.set(false); },
      error: () => { this.snapshotLoading.set(false); },
    });
  }

  // Numeric, language-neutral month labels (01–12) — matches the Payroll Locks page.
  readonly monthOptions = Array.from({ length: 12 }, (_, i) => ({ value: i + 1, label: String(i + 1).padStart(2, '0') }));

  readonly now = new Date();
  // Current year back 5 years — payroll is only viewed for the current or a recent period.
  readonly yearOptions = Array.from({ length: 6 }, (_, i) => {
    const y = this.now.getFullYear() - i;
    return { value: y, label: `${y}` };
  });
  isCurrentOrFutureMonth = computed(() => {
    const y = this.year();
    const m = this.month();
    return y > this.now.getFullYear() ||
      (y === this.now.getFullYear() && m >= this.now.getMonth() + 1);
  });

  load() {
    this.loading.set(true);
    this.adminSvc.getPayrollSummary(this.year(), this.month()).subscribe({
      next: d => { this.data.set(d); this.loading.set(false); },
      error: () => { this.toast.error(this.translate.instant('payroll.loadFailed')); this.loading.set(false); },
    });
  }

  exportCsv() {
    this.exporting.set(true);
    this.adminSvc.exportPayrollCsv(this.year(), this.month()).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = `payroll-${this.year()}-${String(this.month()).padStart(2, '0')}.csv`;
        a.click(); URL.revokeObjectURL(url);
        this.exporting.set(false);
      },
      error: () => { this.toast.error(this.translate.instant('payroll.exportFailed')); this.exporting.set(false); },
    });
  }

  // ── Detailed payslip drill-down (reusable dialog) ────────────────────────────
  private readonly payslipDialog = viewChild.required(PayslipDialogComponent);

  /** Open the payslip for a staff row, defaulting the range to the currently-selected month. */
  openPayslip(row: PayrollStaffRow): void {
    const y = this.year(), m = this.month();
    this.payslipDialog().open(row.staffId, row.name, new Date(y, m - 1, 1), new Date(y, m, 0));
  }
}
