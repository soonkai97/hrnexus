import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { SignalRService, AdminService, DashboardStats, ToastService } from 'shared-lib';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [Tag, Card, Skeleton],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly signalR   = inject(SignalRService);
  private readonly admin     = inject(AdminService);
  private readonly toast     = inject(ToastService);

  stats   = signal<DashboardStats | null>(null);
  loading = signal(true);

  readonly hubConnected = computed(() => this.signalR.isConnected());

  ngOnInit(): void {
    this.admin.getDashboard().subscribe({
      next: d => { this.stats.set(d.stats); this.loading.set(false); },
      error: () => {
        this.toast.error('Failed to load dashboard data.');
        this.loading.set(false);
      },
    });
  }
}
