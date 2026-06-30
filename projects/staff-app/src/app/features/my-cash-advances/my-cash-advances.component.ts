import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CashAdvanceService, CashAdvanceDto, ToastService } from 'shared-lib';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { Skeleton } from 'primeng/skeleton';

@Component({
  selector: 'app-my-cash-advances',
  standalone: true,
  imports: [DecimalPipe, TranslatePipe, Card, Tag, Skeleton],
  templateUrl: './my-cash-advances.component.html',
  styleUrl: './my-cash-advances.component.scss',
})
export class MyCashAdvancesComponent implements OnInit {
  private readonly svc       = inject(CashAdvanceService);
  private readonly toast     = inject(ToastService);
  private readonly translate = inject(TranslateService);

  advances = signal<CashAdvanceDto[]>([]);
  loading  = signal(false);

  readonly totalOutstanding = computed(() =>
    this.advances().filter(a => a.status === 'outstanding').reduce((sum, a) => sum + a.amount, 0));

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.svc.listMine().subscribe({
      next:  list => { this.advances.set(list); this.loading.set(false); },
      error: ()   => { this.toast.error(this.translate.instant('myCashAdvances.loadFailed')); this.loading.set(false); },
    });
  }
}
