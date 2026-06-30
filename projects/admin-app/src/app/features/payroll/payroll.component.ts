import { Component, signal, computed, inject, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, PayrollSummaryResponse, EarningsSnapshotResponse, PayslipResponse,
         PayrollStaffRow, ToastService } from 'shared-lib';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { Message } from 'primeng/message';
import { Dialog } from 'primeng/dialog';
import { DatePicker } from 'primeng/datepicker';
import { Tag } from 'primeng/tag';

@Component({
  selector: 'app-payroll',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, Button, TableModule, Card, Select, Message,
    Dialog, DatePicker, Tag],
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
        this.downloadBlob(blob, `payroll-${this.year()}-${String(this.month()).padStart(2, '0')}.csv`);
        this.exporting.set(false);
      },
      error: () => { this.toast.error(this.translate.instant('payroll.exportFailed')); this.exporting.set(false); },
    });
  }

  // ── Detailed payslip drill-down ─────────────────────────────────────────────
  payslipOpen     = signal(false);
  payslipStaffId  = signal('');
  payslipName     = signal('');
  payslipFrom     = signal<Date | null>(null);   // bound to the from date-picker
  payslipTo       = signal<Date | null>(null);    // bound to the to date-picker
  payslipLoading  = signal(false);
  payslipExporting = signal(false);
  payslip         = signal<PayslipResponse | null>(null);

  /** Open the drill-down for a staff row, defaulting the range to the currently-selected month. */
  openPayslip(row: PayrollStaffRow): void {
    this.payslipStaffId.set(row.staffId);
    this.payslipName.set(row.name);
    const y = this.year(), m = this.month();
    this.payslipFrom.set(new Date(y, m - 1, 1));        // 1st of the selected month
    this.payslipTo.set(new Date(y, m, 0));              // last day of the selected month
    this.payslip.set(null);
    this.payslipOpen.set(true);
    this.loadPayslip();
  }

  /** yyyy-MM-dd in LOCAL time (avoids the UTC shift that toISOString() introduces). */
  private fmt(d: Date): string {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  loadPayslip(): void {
    const from = this.payslipFrom(), to = this.payslipTo();
    if (!from || !to) return;
    if (to < from) { this.toast.error(this.translate.instant('payroll.payslipRangeInvalid')); return; }
    this.payslipLoading.set(true);
    this.adminSvc.getPayslip(this.payslipStaffId(), this.fmt(from), this.fmt(to)).subscribe({
      next: p => { this.payslip.set(p); this.payslipLoading.set(false); },
      error: () => { this.toast.error(this.translate.instant('payroll.payslipLoadFailed')); this.payslipLoading.set(false); },
    });
  }

  exportPayslipCsv(): void {
    const from = this.payslipFrom(), to = this.payslipTo();
    if (!from || !to) return;
    this.payslipExporting.set(true);
    this.adminSvc.exportPayslipCsv(this.payslipStaffId(), this.fmt(from), this.fmt(to)).subscribe({
      next: blob => {
        this.downloadBlob(blob, `payslip-${this.payslipStaffId()}-${this.fmt(from)}-to-${this.fmt(to)}.csv`);
        this.payslipExporting.set(false);
      },
      error: () => { this.toast.error(this.translate.instant('payroll.exportFailed')); this.payslipExporting.set(false); },
    });
  }

  private downloadBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename;
    a.click(); URL.revokeObjectURL(url);
  }
}
