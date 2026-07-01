import { Component, signal, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, PayslipResponse, ToastService, LocalizedNamePipe } from 'shared-lib';
import { Button } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Dialog } from 'primeng/dialog';
import { DatePicker } from 'primeng/datepicker';
import { Tag } from 'primeng/tag';

/**
 * Reusable detailed-payslip dialog: per-shift breakdown for one staff member over a date range,
 * with net-payable, per-job subtotals and a CSV download. Opened imperatively via `open()` from any
 * host (Payroll summary row, Staff Detail header). Keeping it in one component avoids duplicating the
 * markup + load/export logic across pages.
 */
@Component({
  selector: 'app-payslip-dialog',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, Button, TableModule, Dialog, DatePicker, Tag, LocalizedNamePipe],
  templateUrl: './payslip-dialog.component.html',
  styleUrl: './payslip-dialog.component.scss',
})
export class PayslipDialogComponent {
  private readonly adminSvc  = inject(AdminService);
  private readonly toast     = inject(ToastService);
  private readonly translate = inject(TranslateService);

  visible   = signal(false);
  staffId   = signal('');
  name      = signal('');
  from      = signal<Date | null>(null);
  to        = signal<Date | null>(null);
  loading   = signal(false);
  exporting = signal(false);
  payslip   = signal<PayslipResponse | null>(null);

  /**
   * Open the dialog for a staff member. `from`/`to` default to the current calendar month when not
   * supplied (Staff Detail), or the caller passes the selected month's range (Payroll).
   */
  open(staffId: string, staffName: string, from?: Date, to?: Date): void {
    const now = new Date();
    this.staffId.set(staffId);
    this.name.set(staffName);
    this.from.set(from ?? new Date(now.getFullYear(), now.getMonth(), 1));
    this.to.set(to ?? new Date(now.getFullYear(), now.getMonth() + 1, 0));
    this.payslip.set(null);
    this.visible.set(true);
    this.load();
  }

  close(): void { this.visible.set(false); }

  /** yyyy-MM-dd in LOCAL time (avoids the UTC shift that toISOString() introduces). */
  private fmt(d: Date): string {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  load(): void {
    const from = this.from(), to = this.to();
    if (!from || !to) return;
    if (to < from) { this.toast.error(this.translate.instant('payroll.payslipRangeInvalid')); return; }
    this.loading.set(true);
    this.adminSvc.getPayslip(this.staffId(), this.fmt(from), this.fmt(to)).subscribe({
      next: p => { this.payslip.set(p); this.loading.set(false); },
      error: () => { this.toast.error(this.translate.instant('payroll.payslipLoadFailed')); this.loading.set(false); },
    });
  }

  exportCsv(): void {
    const from = this.from(), to = this.to();
    if (!from || !to) return;
    this.exporting.set(true);
    this.adminSvc.exportPayslipCsv(this.staffId(), this.fmt(from), this.fmt(to)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = `payslip-${this.staffId()}-${this.fmt(from)}-to-${this.fmt(to)}.csv`;
        a.click(); URL.revokeObjectURL(url);
        this.exporting.set(false);
      },
      error: () => { this.toast.error(this.translate.instant('payroll.exportFailed')); this.exporting.set(false); },
    });
  }
}
