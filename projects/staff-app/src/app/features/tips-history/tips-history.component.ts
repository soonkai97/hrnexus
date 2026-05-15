import { Component, OnInit, signal, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { PayrollService, PaySummary, MONTH_NAMES } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { InputNumber } from 'primeng/inputnumber';

@Component({
  selector: 'app-tips-history',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, Button, Card, Select, InputNumber],
  templateUrl: './tips-history.component.html',
  styleUrl: './tips-history.component.scss',
})
export class TipsHistoryComponent implements OnInit {
  private readonly payrollSvc = inject(PayrollService);

  selectedYear  = signal(new Date().getFullYear());
  selectedMonth = signal(new Date().getMonth() + 1);
  loading       = signal(false);
  summary       = signal<PaySummary | null>(null);

  readonly months = MONTH_NAMES;
  readonly monthOptions = MONTH_NAMES.map((m, i) => ({ label: m, value: i + 1 }));

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    const y = this.selectedYear();
    const m = this.selectedMonth();
    const start = `${y}-${String(m).padStart(2,'0')}-01`;
    // Use local date components — toISOString() would shift to previous UTC day during 00:00–08:00 KL
    const lastDay = new Date(y, m, 0);
    const end = `${lastDay.getFullYear()}-${String(lastDay.getMonth() + 1).padStart(2, '0')}-${String(lastDay.getDate()).padStart(2, '0')}`;
    this.payrollSvc.getMySummary(start, end).subscribe({
      next: d => { this.summary.set(d); this.loading.set(false); },
      error: () => { this.summary.set(null); this.loading.set(false); },
    });
  }
}
