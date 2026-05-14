import { Component, OnInit, OnDestroy, signal, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { TranslatePipe } from '@ngx-translate/core';
import { SignalRService, AdminService, ActiveStaffDto, RecentEventDto, DashboardStats, ToastService } from 'shared-lib';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly signalR = inject(SignalRService);
  private readonly admin   = inject(AdminService);
  private readonly toast   = inject(ToastService);

  stats        = signal<DashboardStats | null>(null);
  onShift      = signal<ActiveStaffDto[]>([]);
  activity     = signal<(RecentEventDto & { isNew?: boolean })[]>([]);
  loading      = signal(true);
  hubConnected = signal(false);

  private subs = new Subscription();

  constructor() {
    // Reactively sync connection badge — fires whenever hubState signal changes,
    // no polling required.
    effect(() => {
      this.hubConnected.set(this.signalR.isConnected());
    });
  }

  ngOnInit(): void {
    this.loadDashboard();
    this.signalR.startConnection();

    // When live stats arrive, patch numbers and also refresh the lists so the
    // "currently on shift" table and activity feed stay in sync.
    this.subs.add(
      this.signalR.liveStats$.subscribe(s => {
        this.stats.update(cur => cur ? {
          ...cur,
          onShiftCount: s.onShiftCount,
          totalStaff:   s.totalStaff,
          tonightHours: s.tonightHours,
          tonightCost:  s.estimatedLabourCost,
        } : cur);
        // Silently refresh the full dashboard so the table / activity feed update too
        this.loadDashboard(false);
      }),
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
    this.signalR.stopConnection();
  }

  private loadDashboard(showLoader = true): void {
    if (showLoader) this.loading.set(true);
    this.admin.getDashboard().subscribe({
      next: data => {
        this.stats.set(data.stats);
        this.onShift.set(data.currentlyOnShift);
        this.activity.set(data.recentActivity);
        this.loading.set(false);
      },
      error: () => { this.toast.error('Failed to load dashboard data.'); this.loading.set(false); },
    });
  }

  formatMinutes(min: number): string {
    if (min < 60) return `${min}m`;
    const h = Math.floor(min / 60);
    const m = min % 60;
    return m > 0 ? `${h}h ${m}m` : `${h}h`;
  }
}
