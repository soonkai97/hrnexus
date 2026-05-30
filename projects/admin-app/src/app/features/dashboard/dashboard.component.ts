import { Component, OnInit, signal, computed, inject, DestroyRef } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Subject, EMPTY } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SignalRService, AdminService, ActiveStaffDto, RecentEventDto, DashboardStats, ToastService } from 'shared-lib';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, DecimalPipe, TranslatePipe, TableModule, Tag, Card, Skeleton],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly signalR    = inject(SignalRService);
  private readonly admin      = inject(AdminService);
  private readonly toast      = inject(ToastService);
  private readonly translate  = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  stats    = signal<DashboardStats | null>(null);
  onShift  = signal<ActiveStaffDto[]>([]);
  activity = signal<(RecentEventDto & { isNew?: boolean })[]>([]);
  loading  = signal(true);

  // Reads the SignalR connection signal directly — no extra signal + effect needed.
  readonly hubConnected = computed(() => this.signalR.isConnected());

  // switchMap cancels any in-flight getDashboard request when a newer one arrives,
  // preventing stale data from landing after rapid live-stats events.
  private readonly _loadTrigger$ = new Subject<boolean>(); // true = show loader

  constructor() {
    this._loadTrigger$.pipe(
      switchMap(showLoader => {
        if (showLoader) this.loading.set(true);
        return this.admin.getDashboard().pipe(
          catchError(() => {
            this.toast.error(this.translate.instant('dashboard.loadFailed'));
            this.loading.set(false);
            return EMPTY;
          }),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(data => {
      this.stats.set(data.stats);
      this.onShift.set(data.currentlyOnShift);
      this.activity.set(data.recentActivity);
      this.loading.set(false);
    });

    // When live stats arrive, patch numbers immediately (no loader) and also
    // refresh the full dashboard so the table / activity feed stay in sync.
    this.signalR.liveStats$.pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(s => {
      this.stats.update(cur => cur ? {
        ...cur,
        onShiftCount: s.onShiftCount,
        totalStaff:   s.totalStaff,
        tonightHours: s.tonightHours,
        tonightCost:  s.estimatedLabourCost,
      } : cur);
      // switchMap in _loadTrigger$ ensures only the latest refresh wins
      this._loadTrigger$.next(false);
    });
  }

  ngOnInit(): void {
    this._loadTrigger$.next(true); // initial load with spinner
  }

  formatMinutes(min: number): string {
    if (min < 60) return `${min}m`;
    const h = Math.floor(min / 60);
    const m = min % 60;
    return m > 0 ? `${h}h ${m}m` : `${h}h`;
  }
}
