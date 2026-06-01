import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DecimalPipe, DatePipe, UpperCasePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AuthService, LeaveService, WorkLogService, AmPmPipe,
  LeaveBalance, WorkLogDto,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink, DecimalPipe, DatePipe, UpperCasePipe, TranslatePipe, AmPmPipe, Button, Card, Tag],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent implements OnInit {
  private readonly auth            = inject(AuthService);
  private readonly leaveSvc        = inject(LeaveService);
  private readonly workLogSvc      = inject(WorkLogService);

  loading = signal(true);

  // Data signals
  leave         = signal<LeaveBalance | null>(null);
  activeShift   = signal<WorkLogDto | null>(null);
  recentShifts  = signal<WorkLogDto[]>([]);

  // Greeting
  user      = computed(() => this.auth.user());
  firstName = computed(() => this.user()?.fullName.split(' ')[0] ?? '');
  greeting  = computed(() => {
    const h = new Date().getHours();
    if (h < 12) return 'home.goodMorning';
    if (h < 17) return 'home.goodAfternoon';
    return 'home.goodEvening';
  });

  ngOnInit(): void {
    forkJoin({
      leave:       this.leaveSvc.getMyBalance().pipe(catchError(() => of(null))),
      active:      this.workLogSvc.getActive().pipe(catchError(() => of(null))),
      recent:      this.workLogSvc.getMy(1, 5).pipe(catchError(() => of(null))),
    }).subscribe(res => {
      this.leave.set(res.leave);
      this.activeShift.set(res.active);
      this.recentShifts.set(
        (res.recent?.items ?? []).filter(s => !s.isActive).slice(0, 4)
      );
      this.loading.set(false);
    });
  }
}
