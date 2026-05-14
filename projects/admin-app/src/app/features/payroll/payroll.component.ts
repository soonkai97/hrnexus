import { Component, signal, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService, PayrollSummaryResponse, PayrollStaffRow } from 'shared-lib';

@Component({
  selector: 'app-payroll',
  standalone: true,
  imports: [DecimalPipe, FormsModule],
  templateUrl: './payroll.component.html',
  styleUrl: './payroll.component.scss',
})
export class PayrollComponent {
  private readonly adminSvc = inject(AdminService);

  year      = signal(new Date().getFullYear());
  month     = signal(new Date().getMonth() + 1);
  loading   = signal(false);
  exporting = signal(false);
  data      = signal<PayrollSummaryResponse | null>(null);

  readonly months = ['January','February','March','April','May','June','July','August','September','October','November','December'];

  load() {
    this.loading.set(true);
    this.adminSvc.getPayrollSummary(this.year(), this.month()).subscribe({
      next: d => { this.data.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
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
      error: () => this.exporting.set(false),
    });
  }
}
