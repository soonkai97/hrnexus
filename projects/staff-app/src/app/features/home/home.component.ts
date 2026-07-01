import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DecimalPipe, DatePipe, UpperCasePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AuthService, LeaveService, WorkLogService, AmPmPipe,
  LeaveBalance, WorkLogDto, LocalizedNamePipe,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink, DecimalPipe, DatePipe, UpperCasePipe, TranslatePipe, AmPmPipe, LocalizedNamePipe, Button, Card, Tag],
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

  /** Calendar days a shift ran past its work date (0 = same day) — drives the "+N" overnight/multi-day badge. */
  endDayOffset(wl: WorkLogDto): number {
    if (!wl.endDate || wl.endDate === wl.workDate) return 0;
    const ms = new Date(`${wl.endDate}T00:00:00`).getTime() - new Date(`${wl.workDate}T00:00:00`).getTime();
    return Math.max(0, Math.round(ms / 86_400_000));
  }

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
