import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { PayrollService, PaySummary } from 'shared-lib';

@Component({
  selector: 'app-tips-history',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './tips-history.component.html',
  styleUrl: './tips-history.component.scss',
})
export class TipsHistoryComponent implements OnInit {
  private readonly payrollSvc = inject(PayrollService);

  selectedYear  = signal(new Date().getFullYear());
  selectedMonth = signal(new Date().getMonth() + 1);
  loading       = signal(false);
  summary       = signal<PaySummary | null>(null);

  readonly months = [
    'January','February','March','April','May','June',
    'July','August','September','October','November','December',
  ];

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    const y = this.selectedYear();
    const m = this.selectedMonth();
    const start = `${y}-${String(m).padStart(2,'0')}-01`;
    const end   = new Date(y, m, 0).toISOString().split('T')[0];
    this.payrollSvc.getMySummary(start, end).subscribe({
      next: d => { this.summary.set(d); this.loading.set(false); },
      error: () => { this.summary.set(null); this.loading.set(false); },
    });
  }
}
