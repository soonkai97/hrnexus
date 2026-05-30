import { Component, signal, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService, PayrollSummaryResponse, MONTH_NAMES } from 'shared-lib';
import { Button } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { InputNumber } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { Message } from 'primeng/message';

@Component({
  selector: 'app-payroll',
  standalone: true,
  imports: [DecimalPipe, FormsModule, Button, Tag, InputNumber, TableModule, Card, Select, Message],
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

  readonly months = MONTH_NAMES;
  readonly monthOptions = MONTH_NAMES.map((label, i) => ({ label, value: i + 1 }));

  readonly now = new Date();
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
