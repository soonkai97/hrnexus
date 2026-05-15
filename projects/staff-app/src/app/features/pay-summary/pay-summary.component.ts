import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { PayrollService, PaySummary, ToastService, MONTH_NAMES } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { InputNumber } from 'primeng/inputnumber';

@Component({
  selector: 'app-pay-summary',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, TranslatePipe, Button, Card, Select, InputNumber],
  templateUrl: './pay-summary.component.html',
  styleUrl: './pay-summary.component.scss',
})
export class PaySummaryComponent implements OnInit {
  summary = signal<PaySummary | null>(null);
  loading = signal(false);

  selectedYear  = signal(new Date().getFullYear());
  selectedMonth = signal(new Date().getMonth() + 1); // 1-based

  readonly months = MONTH_NAMES;

  readonly monthOptions = MONTH_NAMES.map((m, i) => ({ label: m, value: i + 1 }));

  /** First and last day of the selected month */
  periodStart = computed(() => {
    const y = this.selectedYear(), m = this.selectedMonth();
    return `${y}-${String(m).padStart(2, '0')}-01`;
  });

  periodEnd = computed(() => {
    const y = this.selectedYear(), m = this.selectedMonth();
    // Use local date components — toISOString() would shift to previous UTC day during 00:00–08:00 KL
    const last = new Date(y, m, 0);
    return `${last.getFullYear()}-${String(last.getMonth() + 1).padStart(2, '0')}-${String(last.getDate()).padStart(2, '0')}`;
  });

  private readonly toast = inject(ToastService);

  constructor(private payroll: PayrollService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.payroll.getMySummary(this.periodStart(), this.periodEnd()).subscribe({
      next: s => { this.summary.set(s); this.loading.set(false); },
      error: () => { this.summary.set(null); this.loading.set(false); this.toast.error('Failed to load pay summary.'); },
    });
  }

  printPaySlip(): void { window.print(); }
}
