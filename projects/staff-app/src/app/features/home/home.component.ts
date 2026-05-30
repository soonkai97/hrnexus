import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DecimalPipe, DatePipe, UpperCasePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AuthService, PayrollService, LeaveService, WorkLogService,
  FeatureFlagService, AnnouncementService, AmPmPipe,
  PaySummary, LeaveBalance, WorkLogDto, toLocalDateString,
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
  private readonly payrollSvc      = inject(PayrollService);
  private readonly leaveSvc        = inject(LeaveService);
  private readonly workLogSvc      = inject(WorkLogService);
  private readonly featureSvc      = inject(FeatureFlagService);
  private readonly announcementSvc = inject(AnnouncementService);

  loading = signal(true);

  // Data signals
  pay           = signal<PaySummary | null>(null);
  leave         = signal<LeaveBalance | null>(null);
  activeShift   = signal<WorkLogDto | null>(null);
  recentShifts  = signal<WorkLogDto[]>([]);
  unreadNotices = signal(0);

  // Feature flags
  showPay           = computed(() => this.featureSvc.isEnabled('paySummary'));
  showAnnouncements = computed(() => this.featureSvc.isEnabled('announcements'));

  // Greeting
  user      = computed(() => this.auth.user());
  firstName = computed(() => this.user()?.fullName.split(' ')[0] ?? '');
  greeting  = computed(() => {
    const h = new Date().getHours();
    if (h < 12) return 'home.goodMorning';
    if (h < 17) return 'home.goodAfternoon';
    return 'home.goodEvening';
  });

  // This month's pay period
  private get monthStart(): string {
    const d = new Date();
    return toLocalDateString(new Date(d.getFullYear(), d.getMonth(), 1));
  }
  private get monthEnd(): string {
    const d = new Date();
    return toLocalDateString(new Date(d.getFullYear(), d.getMonth() + 1, 0));
  }

  ngOnInit(): void {
    const pay$  = this.showPay()
      ? this.payrollSvc.getMySummary(this.monthStart, this.monthEnd).pipe(catchError(() => of(null)))
      : of(null);

    forkJoin({
      pay:         pay$,
      leave:       this.leaveSvc.getMyBalance().pipe(catchError(() => of(null))),
      active:      this.workLogSvc.getActive().pipe(catchError(() => of(null))),
      recent:      this.workLogSvc.getMy(1, 5).pipe(catchError(() => of(null))),
      notices:     this.showAnnouncements()
                     ? this.announcementSvc.getMine().pipe(catchError(() => of([])))
                     : of([]),
    }).subscribe(res => {
      this.pay.set(res.pay);
      this.leave.set(res.leave);
      this.activeShift.set(res.active);
      this.recentShifts.set(
        (res.recent?.items ?? []).filter(s => !s.isActive).slice(0, 4)
      );
      this.unreadNotices.set(
        Array.isArray(res.notices) ? res.notices.filter((a: { isRead: boolean }) => !a.isRead).length : 0
      );
      this.loading.set(false);
    });
  }
}
