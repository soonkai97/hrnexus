import { Component, OnInit, signal, computed, inject, DestroyRef } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Subject, EMPTY, forkJoin, of } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SignalRService, AdminService, WorkLogService, ActiveStaffDto, RecentEventDto, DashboardStats, EarningsSnapshotResponse, ToastService, apiErrorMessage, nowRoundedToHalfHour, LocalizedNamePipe, StaffStore } from 'shared-lib';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { DatePicker } from 'primeng/datepicker';
import { TooltipModule } from 'primeng/tooltip';
import { PrimeTemplate } from 'primeng/api';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, TranslatePipe, TableModule, Tag, Card, Skeleton, Button, Dialog, DatePicker, TooltipModule, PrimeTemplate, LocalizedNamePipe],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly signalR    = inject(SignalRService);
  private readonly admin      = inject(AdminService);
  private readonly workLog    = inject(WorkLogService);
  private readonly toast      = inject(ToastService);
  private readonly translate  = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly staffStore = inject(StaffStore);

  stats    = signal<DashboardStats | null>(null);
  onShift  = signal<ActiveStaffDto[]>([]);
  activity = signal<(RecentEventDto & { isNew?: boolean })[]>([]);
  // Month-to-date payroll figures (earned this month + outstanding advances) for the financial
  // cards. Null when not yet loaded OR the caller lacks payroll.view (the snapshot endpoint 403s,
  // which we swallow) — the cards just don't render in that case.
  earnings = signal<EarningsSnapshotResponse | null>(null);
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
        // Load the dashboard + the month-to-date earnings snapshot together. The earnings call is
        // made resilient (caller may lack payroll.view) so a 403/error there never breaks the
        // dashboard — it just leaves the financial cards hidden.
        return forkJoin({
          dash: this.admin.getDashboard(),
          earn: this.admin.getEarningsSnapshot().pipe(catchError(() => of(null))),
        }).pipe(
          catchError(() => {
            this.toast.error(this.translate.instant('dashboard.loadFailed'));
            this.loading.set(false);
            return EMPTY;
          }),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(({ dash, earn }) => {
      this.stats.set(dash.stats);
      this.onShift.set(dash.currentlyOnShift);
      this.activity.set(dash.recentActivity);
      this.earnings.set(earn);
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
    this.staffStore.loadRoles();   // roles carry payType → drives the close-shift end-time step
  }

  formatMinutes(min: number): string {
    if (min < 60) return `${min}m`;
    const h = Math.floor(min / 60);
    const m = min % 60;
    return m > 0 ? `${h}h ${m}m` : `${h}h`;
  }

  /** Effective pin for a shift — the latest "Update location" fix if the staff pushed one,
   *  otherwise the position captured at clock-in. Null when no GPS was ever captured. */
  private coords(s: ActiveStaffDto): { lat: number; lng: number } | null {
    const lat = s.lastLatitude ?? s.startLatitude;
    const lng = s.lastLongitude ?? s.startLongitude;
    return lat != null && lng != null ? { lat, lng } : null;
  }

  /** Whether this shift has any GPS to link to. */
  hasLocation(s: ActiveStaffDto): boolean { return this.coords(s) != null; }

  /** Google Maps link for the shift's current (or clock-in) position. */
  mapsUrl(s: ActiveStaffDto): string {
    const c = this.coords(s);
    return c ? `https://www.google.com/maps?q=${c.lat},${c.lng}` : '';
  }

  // ── Close a forgotten clock-out ───────────────────────────────────────────────
  // An admin sets the real end time on an open shift (staff forgot to clock out).
  closeRow      = signal<ActiveStaffDto | null>(null);
  closeEndDate: Date | null = null;
  closeEndTime: Date | null = null;
  closeSaving   = signal(false);

  /** Minute step for the close-shift END picker — from the shift's job pay type (FIXED = 10-min,
   *  HOURLY = 30-min block), matched by the frozen role name against the loaded roles. Unknown → 30. */
  get closeEndStep(): number {
    const row = this.closeRow();
    const role = row ? this.staffStore.roles().find(r => r.name === row.role) : undefined;
    return role?.payType === 'fixed' ? 10 : 30;
  }

  openCloseShift(row: ActiveStaffDto): void {
    this.closeRow.set(row);
    // Default the end date to the shift's clock-in date (the common case is a same-day forgotten
    // clock-out); the admin can move it to the next day for an overnight shift. Time defaults to now.
    this.closeEndDate = new Date(`${row.workDate}T00:00:00`);
    this.closeEndTime = nowRoundedToHalfHour();
    this.closeSaving.set(false);
  }

  cancelCloseShift(): void {
    this.closeRow.set(null);
    this.closeEndDate = null;
    this.closeEndTime = null;
  }

  confirmCloseShift(): void {
    const row = this.closeRow();
    if (!row || !this.closeEndDate || !this.closeEndTime) return;
    const y  = this.closeEndDate.getFullYear();
    const mo = String(this.closeEndDate.getMonth() + 1).padStart(2, '0');
    const d  = String(this.closeEndDate.getDate()).padStart(2, '0');
    const hh = String(this.closeEndTime.getHours()).padStart(2, '0');
    const mm = String(this.closeEndTime.getMinutes()).padStart(2, '0');
    this.closeSaving.set(true);
    this.workLog.adminEndShift(row.workLogId, `${y}-${mo}-${d}`, `${hh}:${mm}`).subscribe({
      next: () => {
        this.closeSaving.set(false);
        this.cancelCloseShift();
        this.toast.success(this.translate.instant('dashboard.shiftClosedToast'));
        this._loadTrigger$.next(false);   // refresh the dashboard so the closed shift drops off
      },
      error: (e: unknown) => {
        this.closeSaving.set(false);
        this.toast.error(apiErrorMessage(e) ?? this.translate.instant('dashboard.shiftCloseFailed'));
      },
    });
  }
}
