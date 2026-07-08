import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { WorkLogService, WorkLogDto, LocationStore, ToastService, AuthService, HmPipe, todayLocalString, nowRoundedToHalfHour, TrimInputDirective, CanComponentDeactivate, LangService, localizedNameFor } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { Paginator } from 'primeng/paginator';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';

type Mode = 'live' | 'manual';

function nowHHmm(): string {
  const d = new Date();
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
}

@Component({
  selector: 'app-log-shift',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, HmPipe, TrimInputDirective, Button, Card, Tag, InputText, Select, Message, Skeleton, DatePicker, Tabs, TabList, Tab, Paginator],
  templateUrl: './log-shift.component.html',
  styleUrl: './log-shift.component.scss',
})
export class LogShiftComponent implements OnInit, CanComponentDeactivate {
  private readonly workLogSvc    = inject(WorkLogService);
  private readonly locationStore = inject(LocationStore);
  private readonly toast         = inject(ToastService);
  readonly auth                  = inject(AuthService);
  private readonly translate     = inject(TranslateService);
  private readonly lang          = inject(LangService);

  mode = signal<Mode>('live');
  view = signal<'log' | 'history'>('log');

  get viewOptions() {
    return [
      { label: this.translate.instant('nav.logShift'), value: 'log'     },
      { label: this.translate.instant('logShift.history'), value: 'history' },
    ];
  }
  get modeOptions() {
    return [
      { label: this.translate.instant('logShift.startNow'),    value: 'live'   },
      { label: this.translate.instant('logShift.manualEntry'), value: 'manual' },
    ];
  }

  // State
  activeLog    = signal<WorkLogDto | null>(null);
  history      = signal<WorkLogDto[]>([]);
  totalHistory = signal(0);
  historyPage  = signal(1);
  readonly historyPageSize = 10;
  totalHistoryPages = computed(() => Math.max(1, Math.ceil(this.totalHistory() / this.historyPageSize)));
  loading    = signal(false);
  submitting = signal(false);
  updatingLocation = signal(false);

  // Only show jobs assigned to the current staff member (from the login/refresh `jobs` payload).
  // The SUPERADMIN tier isn't a real clock-in job, so it's filtered out by name. Options carry the
  // job's GUID id as the value (submitted on clock-in) and a language-aware display label — so a
  // job with a non-ASCII name (e.g. 写钟（在外）) is picked by id and never string-matched.
  readonly myJobs = computed(() =>
    (this.auth.user()?.jobs ?? []).filter(j => j.name !== 'SUPERADMIN')
  );

  readonly myRoleOptions = computed(() => {
    const lang = this.lang.current();
    return this.myJobs().map(j => ({
      label: localizedNameFor(lang, j.name, j.nameZh, j.nameVi),
      value: j.id,
    }));
  });

  // Staff may only log shifts at the locations they're assigned to.
  // Label follows the UI language (Chinese/Vietnamese name when set, else the primary name).
  readonly locationOptions = computed(() => {
    const lang = this.lang.current();
    return this.locationStore.myLocations().map(l => ({
      label: localizedNameFor(lang, l.name, l.nameZh, l.nameVi),
      value: l.id,
    }));
  });

  readonly locations = this.locationStore.myLocations;
  /** True once the assigned-locations fetch has returned and found none — blocks shift logging. */
  readonly noLocations = computed(() =>
    this.locationStore.myLoaded() && this.locationStore.myLocations().length === 0
  );

  // Live form
  liveForm = {
    roleId:     '',
    locationId: '',
    startTime:  nowHHmm(), // kept for API compat; template uses liveFormStartTime
    notes:      '',
  };
  liveFormStartTime: Date | null = nowRoundedToHalfHour(); // UI binding for live start time

  // End shift
  endTime = nowHHmm(); // kept for API compat; template uses endTimeDate
  endingId = signal<string | null>(null);
  endTimeDate: Date | null = null; // UI binding for end time
  endDateStr  = todayLocalString(); // UI binding for end date — defaults to the clock-out day so a
                                     // shift can span several days; staff only change it for an edge case.

  /** Minute step for a check-out picker, chosen by the job's billing granularity: FIXED packages
   *  allow fine 10-min adjustments (the exact minutes don't change their flat pay), while HOURLY jobs
   *  snap to the 30-min billing block so a checkout lands on a clean half-hour. Unknown/legacy → 30. */
  private stepFor(payType: string | null | undefined): number {
    return payType === 'fixed' ? 10 : 30;
  }

  /** Step for the LIVE check-out picker — from the active shift's job (matched by its frozen name). */
  get endStep(): number {
    const active = this.activeLog();
    const job = active ? this.myJobs().find(j => j.name === active.roleName) : undefined;
    return this.stepFor(job?.payType);
  }

  /** Start pickers (live + manual) intentionally IGNORE pay type — by requirement they always step
   *  in a fixed 30-min grid. Only the END/check-out picker follows the job's billing granularity. */
  private readonly startStep = 30;

  /** Step for the MANUAL end/check-out picker — from the job selected in the form (fixed=10 / hourly=30). */
  get manualStep(): number {
    const job = this.myJobs().find(j => j.id === this.manualForm.roleId);
    return this.stepFor(job?.payType);
  }

  /** Snap a time to the nearest multiple of `step` minutes (seconds zeroed). 60 rolls to the next
   *  hour, so e.g. 11:55 @30 → 12:00. Keeps manual times on clean grid lines. */
  private roundToStep(d: Date, step: number): Date {
    const r = new Date(d);
    r.setSeconds(0, 0);
    r.setMinutes(Math.round(r.getMinutes() / step) * step);
    return r;
  }

  /** Neutral spinner anchor for the manual time pickers — a fixed 12:00, NOT the current time, so an
   *  empty picker never opens on "now" (a manual entry is a past shift, not now). The field keeps its
   *  "Select time" placeholder until the staff actually picks a value. */
  readonly manualPickerDefault = (() => { const d = new Date(); d.setHours(12, 0, 0, 0); return d; })();

  /** On job change, re-snap an ALREADY-picked end time to the new job's block (pay type may have
   *  changed). Does NOT seed empty pickers — manual times stay blank until entered and never default
   *  to the current time. */
  onManualJobChange(): void {
    if (this.manualFormEndTime) {
      this.manualFormEndTime = this.roundToStep(this.manualFormEndTime, this.manualStep);
    }
  }

  /** Snap the picked manual time so it never lands on a stray minute (e.g. 11:07). PrimeNG's
   *  stepMinute only sizes the arrows — it doesn't snap the value. Start snaps to the fixed 30-min
   *  grid (pay-type-independent); end snaps to the job's billing block. */
  onManualStartChange(d: Date | null): void {
    this.manualFormStartTime = d ? this.roundToStep(d, this.startStep) : null;
  }
  onManualEndChange(d: Date | null): void {
    this.manualFormEndTime = d ? this.roundToStep(d, this.manualStep) : null;
  }

  // Manual form
  manualForm = {
    roleId:     '',
    locationId: '',
    workDate:   todayLocalString(),
    startTime:  '', // kept for API compat; template uses manualFormStartTime
    endTime:    '', // kept for API compat; template uses manualFormEndTime
    endDate:    '', // yyyy-MM-dd; blank = same-day/overnight (backend infers), set for a multi-day shift
    notes:      '',
  };
  manualFormStartTime: Date | null = null; // UI binding
  manualFormEndTime:   Date | null = null; // UI binding

  hasUnsavedChanges(): boolean {
    if (this.view() !== 'log' || !!this.activeLog()) return false;
    if (this.mode() === 'live') {
      return !!(this.liveForm.roleId || this.liveForm.locationId || this.liveForm.notes);
    }
    // manual mode
    return !!(this.manualForm.roleId || this.manualForm.locationId ||
              this.manualFormStartTime || this.manualFormEndTime || this.manualForm.notes);
  }

  get isOvernightManual(): boolean {
    if (!this.manualFormStartTime || !this.manualFormEndTime) return false;
    const sm = this.manualFormStartTime.getHours() * 60 + this.manualFormStartTime.getMinutes();
    const em = this.manualFormEndTime.getHours() * 60 + this.manualFormEndTime.getMinutes();
    return em < sm;
  }

  /** The calendar date on which an overnight manual shift ends (WorkDate + 1). */
  get manualEndDate(): string | null {
    if (!this.manualForm.workDate || !this.isOvernightManual) return null;
    const d = new Date(this.manualForm.workDate);
    d.setDate(d.getDate() + 1);
    return d.toISOString().slice(0, 10);
  }

  // Error
  formError = signal('');

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  /** How many calendar days after the work date the shift ended (0 = same day). Drives the "+N"
   *  badge so an overnight/multi-day shift reads correctly instead of looking like end-before-start. */
  /** The job name shown in the viewer's language (Chinese/Vietnamese when set, else the primary/English
   *  snapshot). A renamed/deleted job has no localized names → falls back to the frozen roleName. */
  roleLabel(wl: WorkLogDto): string {
    return localizedNameFor(this.lang.current(), wl.roleName, wl.roleNameZh ?? null, wl.roleNameVi ?? null);
  }

  /** The location name shown in the viewer's language (Chinese/Vietnamese when set, else the primary/
   *  English snapshot). A deleted location has no localized names → falls back to the frozen locationName. */
  locationLabel(wl: WorkLogDto): string {
    return localizedNameFor(this.lang.current(), wl.locationName, wl.locationNameZh ?? null, wl.locationNameVi ?? null);
  }

  endDayOffset(wl: WorkLogDto): number {
    if (!wl.endDate || wl.endDate === wl.workDate) return 0;
    const ms = new Date(`${wl.endDate}T00:00:00`).getTime() - new Date(`${wl.workDate}T00:00:00`).getTime();
    return Math.max(0, Math.round(ms / 86_400_000));
  }

  ngOnInit(): void {
    this.locationStore.loadMine();
    this.loadActive();
    this.loadHistory();
  }

  loadActive(): void {
    this.workLogSvc.getActive().subscribe({
      next: a => this.activeLog.set(a),
      error: () => {},
    });
  }

  loadHistory(page = 1): void {
    this.loading.set(true);
    this.historyPage.set(page);
    this.workLogSvc.getMy(page, this.historyPageSize).subscribe({
      next: r => {
        const activeCount = r.items.filter(l => l.isActive).length;
        this.history.set(r.items.filter(l => !l.isActive));
        this.totalHistory.set(r.total - activeCount);
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); },
    });
  }

  setMode(m: Mode): void {
    this.mode.set(m);
    this.formError.set('');
  }

  // ── Live: start shift ────────────────────────────────────────────────────────

  startShift(): void {
    if (!this.liveForm.roleId || !this.liveForm.locationId) {
      this.formError.set(this.translate.instant('logShift.errSelectRoleLocation')); return;
    }
    const startTime = this.liveFormStartTime ? this.toTimeStr(this.liveFormStartTime) : nowHHmm();
    this.submitting.set(true);
    this.formError.set('');
    // Capture the device's GPS first so admin can see where the shift was started.
    // Best-effort: if the staff denies permission or it's unavailable, clock in without it.
    this.getDeviceCoords().then(coords => {
      this.workLogSvc.start({
        roleId:     this.liveForm.roleId,
        locationId: this.liveForm.locationId,
        startTime,
        notes:      this.liveForm.notes || null,
        latitude:   coords?.lat ?? null,
        longitude:  coords?.lng ?? null,
      }).subscribe({
        next: () => {
          this.submitting.set(false);
          this.toast.success(this.translate.instant('logShift.toast.started'));
          this.loadActive();
          this.liveForm = { roleId: '', locationId: '', startTime: nowHHmm(), notes: '' };
          this.liveFormStartTime = nowRoundedToHalfHour();
        },
        error: (e) => {
          this.formError.set(e?.error?.error ?? this.translate.instant('logShift.errStart'));
          this.submitting.set(false);
          // If the backend detected an active shift that we missed (e.g. loadActive()
          // failed silently on page load), refresh now so the End Shift card appears.
          this.loadActive();
        },
      });
    });
  }

  /** "Update location" — capture a fresh GPS fix and push it to the active shift. Best-effort:
   *  if the staff denies permission or it's unavailable, tell them rather than failing silently. */
  updateLocation(): void {
    if (this.updatingLocation()) return;
    this.updatingLocation.set(true);
    this.getDeviceCoords().then(coords => {
      if (!coords) {
        this.updatingLocation.set(false);
        this.toast.warning(this.translate.instant('logShift.locationUnavailable'));
        return;
      }
      this.workLogSvc.updateActiveLocation(coords.lat, coords.lng).subscribe({
        next:  () => { this.updatingLocation.set(false); this.toast.success(this.translate.instant('logShift.locationUpdated')); },
        error: (e) => { this.updatingLocation.set(false); this.toast.error(e?.error?.error ?? this.translate.instant('logShift.locationUpdateFailed')); },
      });
    });
  }

  /**
   * Resolves the device's current GPS position, or null if geolocation is unsupported,
   * denied, or times out. Never rejects — a missing location must not block clocking in.
   */
  private getDeviceCoords(): Promise<{ lat: number; lng: number } | null> {
    return new Promise(resolve => {
      if (!('geolocation' in navigator)) { resolve(null); return; }
      navigator.geolocation.getCurrentPosition(
        pos => resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
        ()  => resolve(null),
        { enableHighAccuracy: true, timeout: 8000, maximumAge: 60000 },
      );
    });
  }

  // ── Live: end shift ──────────────────────────────────────────────────────────

  openEndShift(id: string): void {
    this.endDateStr  = todayLocalString();
    this.endTimeDate = nowRoundedToHalfHour();
    this.endingId.set(id);
  }

  cancelEnd(): void {
    this.endTimeDate = null;
    this.endingId.set(null);
  }

  confirmEnd(): void {
    const id = this.endingId();
    if (!id) return;
    const endTime = this.endTimeDate ? this.toTimeStr(this.endTimeDate) : nowHHmm();
    this.submitting.set(true);
    this.workLogSvc.end(id, endTime, this.endDateStr).subscribe({
      next: () => {
        this.submitting.set(false);
        this.endingId.set(null);
        this.endTimeDate = null;
        this.toast.success(this.translate.instant('logShift.toast.ended'));
        this.activeLog.set(null);
        this.loadHistory();
      },
      error: (e) => {
        this.toast.error(e?.error?.error ?? this.translate.instant('logShift.errEnd'));
        this.submitting.set(false);
      },
    });
  }

  // ── Manual entry ─────────────────────────────────────────────────────────────

  submitManual(): void {
    if (!this.manualForm.roleId || !this.manualForm.locationId || !this.manualForm.workDate || !this.manualFormStartTime || !this.manualFormEndTime) {
      this.formError.set(this.translate.instant('logShift.errFillAll')); return;
    }
    // Convert Date → HH:mm for validation and API
    const startTime = this.toTimeStr(this.manualFormStartTime);
    const endTime   = this.toTimeStr(this.manualFormEndTime);
    const [sh = 0, sm = 0] = startTime.split(':').map(Number);
    const [eh = 0, em = 0] = endTime.split(':').map(Number);
    const startMins = sh * 60 + sm;
    const endMins   = eh * 60 + em;
    // A blank end date means a same-day/overnight shift — the backend infers the end date (≤24h) and
    // rejects a zero-duration entry. An explicit end date logs a multi-day shift; the 30-day cap is
    // enforced by the backend, so here we only catch the obvious end-before-start mistakes.
    const endDate = this.manualForm.endDate || null;
    if (endDate) {
      if (endDate < this.manualForm.workDate) {
        this.formError.set(this.translate.instant('logShift.errEndBeforeStart')); return;
      }
      if (endDate === this.manualForm.workDate && endMins <= startMins) {
        this.formError.set(this.translate.instant('logShift.errSameTime')); return;
      }
    } else if (startMins === endMins) {
      this.formError.set(this.translate.instant('logShift.errSameTime')); return;
    }
    this.submitting.set(true);
    this.formError.set('');
    this.workLogSvc.createManual({
      roleId:     this.manualForm.roleId,
      locationId: this.manualForm.locationId,
      workDate:   this.manualForm.workDate,
      startTime,
      endTime,
      endDate,
      notes:      this.manualForm.notes || null,
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.toast.success(this.translate.instant('logShift.toast.logged'));
        this.manualForm = { roleId: '', locationId: '', workDate: todayLocalString(), startTime: '', endTime: '', endDate: '', notes: '' };
        this.manualFormStartTime = null;
        this.manualFormEndTime   = null;
        this.loadHistory();
      },
      error: (e) => {
        this.formError.set(e?.error?.error ?? this.translate.instant('logShift.errLog'));
        this.submitting.set(false);
      },
    });
  }

}
