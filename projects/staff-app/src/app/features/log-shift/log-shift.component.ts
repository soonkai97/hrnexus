import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DecimalPipe, UpperCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { WorkLogService, WorkLogDto, UpdateWorkLogRequest, LocationStore, ToastService, ConfirmService, AuthService, AmPmPipe, todayLocalString, nowRoundedToHalfHour, TrimInputDirective, CanComponentDeactivate } from 'shared-lib';
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
  imports: [DecimalPipe, UpperCasePipe, FormsModule, TranslatePipe, AmPmPipe, TrimInputDirective, Button, Card, Tag, InputText, Select, Message, Skeleton, DatePicker, Tabs, TabList, Tab, Paginator],
  templateUrl: './log-shift.component.html',
  styleUrl: './log-shift.component.scss',
})
export class LogShiftComponent implements OnInit, CanComponentDeactivate {
  private readonly workLogSvc    = inject(WorkLogService);
  private readonly locationStore = inject(LocationStore);
  private readonly toast         = inject(ToastService);
  private readonly confirmSvc    = inject(ConfirmService);
  readonly auth                  = inject(AuthService);
  private readonly translate     = inject(TranslateService);

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

  // Only show roles assigned to the current staff member
  readonly myRoles = computed(() => {
    const assigned = this.auth.user()?.roles ?? [];
    return assigned.filter(r => r !== 'SUPERADMIN');
  });

  readonly myRoleOptions = computed(() =>
    this.myRoles().map(r => ({ label: r, value: r }))
  );

  // Staff may only log shifts at the locations they're assigned to.
  readonly locationOptions = computed(() =>
    this.locationStore.myLocations().map(l => ({ label: l.name, value: l.id }))
  );

  readonly locations = this.locationStore.myLocations;
  /** True once the assigned-locations fetch has returned and found none — blocks shift logging. */
  readonly noLocations = computed(() =>
    this.locationStore.myLoaded() && this.locationStore.myLocations().length === 0
  );

  // Live form
  liveForm = {
    roleName:   '',
    locationId: '',
    startTime:  nowHHmm(), // kept for API compat; template uses liveFormStartTime
    notes:      '',
  };
  liveFormStartTime: Date | null = nowRoundedToHalfHour(); // UI binding for live start time

  // End shift
  endTime = nowHHmm(); // kept for API compat; template uses endTimeDate
  endingId = signal<string | null>(null);
  endTimeDate: Date | null = null; // UI binding for end time

  // Manual form
  manualForm = {
    roleName:   '',
    locationId: '',
    workDate:   todayLocalString(),
    startTime:  '', // kept for API compat; template uses manualFormStartTime
    endTime:    '', // kept for API compat; template uses manualFormEndTime
    notes:      '',
  };
  manualFormStartTime: Date | null = null; // UI binding
  manualFormEndTime:   Date | null = null; // UI binding

  hasUnsavedChanges(): boolean {
    if (this.view() !== 'log' || !!this.activeLog()) return false;
    if (this.mode() === 'live') {
      return !!(this.liveForm.roleName || this.liveForm.locationId || this.liveForm.notes);
    }
    // manual mode
    return !!(this.manualForm.roleName || this.manualForm.locationId ||
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

  get isOvernightEdit(): boolean {
    if (!this.editFormStartTime || !this.editFormEndTime) return false;
    const sm = this.editFormStartTime.getHours() * 60 + this.editFormStartTime.getMinutes();
    const em = this.editFormEndTime.getHours() * 60 + this.editFormEndTime.getMinutes();
    return em < sm;
  }

  /** The calendar date on which an overnight edited shift ends (WorkDate + 1). */
  get editEndDate(): string | null {
    if (!this.editForm.workDate || !this.isOvernightEdit) return null;
    const d = new Date(this.editForm.workDate);
    d.setDate(d.getDate() + 1);
    return d.toISOString().slice(0, 10);
  }

  // Error
  formError = signal('');

  deletingId = signal<string | null>(null);

  // ── Edit ─────────────────────────────────────────────────────────────────────
  editingLogId = signal<string | null>(null);
  editSaving   = signal(false);
  editError    = signal('');
  editForm: UpdateWorkLogRequest = { roleName: '', locationId: '', workDate: '', startTime: '', endDate: '', endTime: '', notes: '' };
  editFormStartTime: Date | null = null; // UI binding
  editFormEndTime:   Date | null = null; // UI binding

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  /** How many calendar days after the work date the shift ended (0 = same day). Drives the "+N"
   *  badge so an overnight/multi-day shift reads correctly instead of looking like end-before-start. */
  endDayOffset(wl: WorkLogDto): number {
    if (!wl.endDate || wl.endDate === wl.workDate) return 0;
    const ms = new Date(`${wl.endDate}T00:00:00`).getTime() - new Date(`${wl.workDate}T00:00:00`).getTime();
    return Math.max(0, Math.round(ms / 86_400_000));
  }

  /** yyyy-MM-dd of the day after the given yyyy-MM-dd (used to infer an overnight end date). */
  private nextDay(dateStr: string): string {
    const d = new Date(`${dateStr}T00:00:00`);
    d.setDate(d.getDate() + 1);
    return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
  }

  private parseTime(t: string): Date | null {
    if (!t) return null;
    const [h = NaN, m = NaN] = t.split(':').map(Number);
    if (isNaN(h) || isNaN(m)) return null;
    const d = new Date();
    d.setHours(h, m, 0, 0);
    return d;
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
    if (!this.liveForm.roleName || !this.liveForm.locationId) {
      this.formError.set(this.translate.instant('logShift.errSelectRoleLocation')); return;
    }
    const startTime = this.liveFormStartTime ? this.toTimeStr(this.liveFormStartTime) : nowHHmm();
    this.submitting.set(true);
    this.formError.set('');
    this.workLogSvc.start({
      roleName:   this.liveForm.roleName,
      locationId: this.liveForm.locationId,
      startTime,
      notes:      this.liveForm.notes || null,
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.toast.success(this.translate.instant('logShift.toast.started'));
        this.loadActive();
        this.liveForm = { roleName: '', locationId: '', startTime: nowHHmm(), notes: '' };
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
  }

  // ── Live: end shift ──────────────────────────────────────────────────────────

  openEndShift(id: string): void {
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
    this.workLogSvc.end(id, endTime).subscribe({
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
    if (!this.manualForm.roleName || !this.manualForm.locationId || !this.manualForm.workDate || !this.manualFormStartTime || !this.manualFormEndTime) {
      this.formError.set(this.translate.instant('logShift.errFillAll')); return;
    }
    // Convert Date → HH:mm for validation and API
    const startTime = this.toTimeStr(this.manualFormStartTime);
    const endTime   = this.toTimeStr(this.manualFormEndTime);
    const [sh = 0, sm = 0] = startTime.split(':').map(Number);
    const [eh = 0, em = 0] = endTime.split(':').map(Number);
    const startMins = sh * 60 + sm;
    const endMins   = eh * 60 + em;
    // >= so same-time gives 0 (not 1440); overnight gives [1, 1439]
    const durationMins = endMins >= startMins ? endMins - startMins : endMins + 1440 - startMins;
    if (durationMins === 0) {
      this.formError.set(this.translate.instant('logShift.errSameTime')); return;
    }
    if (durationMins > 1440) {
      this.formError.set(this.translate.instant('logShift.errMax24')); return;
    }
    this.submitting.set(true);
    this.formError.set('');
    this.workLogSvc.createManual({
      roleName:   this.manualForm.roleName,
      locationId: this.manualForm.locationId,
      workDate:   this.manualForm.workDate,
      startTime,
      endTime,
      notes:      this.manualForm.notes || null,
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.toast.success(this.translate.instant('logShift.toast.logged'));
        this.manualForm = { roleName: '', locationId: '', workDate: todayLocalString(), startTime: '', endTime: '', notes: '' };
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

  // ── Edit ─────────────────────────────────────────────────────────────────────

  openEditLog(wl: WorkLogDto): void {
    this.editForm = {
      roleName:   wl.roleName.toUpperCase(),
      locationId: wl.locationId,
      workDate:   wl.workDate,
      startTime:  wl.startTime,
      endDate:    wl.endDate ?? wl.workDate,
      endTime:    wl.endTime ?? '',
      notes:      wl.notes ?? '',
    };
    this.editFormStartTime = this.parseTime(wl.startTime);
    this.editFormEndTime   = this.parseTime(wl.endTime ?? '');
    this.editError.set('');
    this.editingLogId.set(wl.id);
  }

  cancelEdit(): void {
    this.editingLogId.set(null);
    this.editFormStartTime = null;
    this.editFormEndTime   = null;
  }

  saveEdit(): void {
    const id = this.editingLogId();
    if (!id) return;
    if (!this.editForm.roleName || !this.editForm.locationId || !this.editForm.workDate || !this.editFormStartTime || !this.editFormEndTime) {
      this.editError.set(this.translate.instant('logShift.errFillRequired')); return;
    }
    const startTime = this.toTimeStr(this.editFormStartTime);
    const endTime   = this.toTimeStr(this.editFormEndTime);
    const [sh = 0, sm = 0] = startTime.split(':').map(Number);
    const [eh = 0, em = 0] = endTime.split(':').map(Number);
    const durationMins = (eh * 60 + em) >= (sh * 60 + sm)
      ? (eh * 60 + em) - (sh * 60 + sm)
      : (eh * 60 + em) + 1440 - (sh * 60 + sm);
    if (durationMins === 0) { this.editError.set(this.translate.instant('logShift.errSameTimeEdit')); return; }
    if (durationMins > 1440) { this.editError.set(this.translate.instant('logShift.errMax24')); return; }

    // Staff edits stay single-overnight (≤24h): infer the end date (next day when the shift wraps).
    const endDate = (eh * 60 + em) <= (sh * 60 + sm) ? this.nextDay(this.editForm.workDate) : this.editForm.workDate;

    this.editSaving.set(true);
    this.editError.set('');
    this.workLogSvc.update(id, { ...this.editForm, startTime, endDate, endTime, notes: this.editForm.notes || null }).subscribe({
      next: () => {
        this.editSaving.set(false);
        this.editingLogId.set(null);
        this.editFormStartTime = null;
        this.editFormEndTime   = null;
        this.toast.success(this.translate.instant('logShift.toast.updated'));
        this.loadHistory();
      },
      error: (e) => {
        this.editError.set(e?.error?.error ?? this.translate.instant('logShift.errUpdate'));
        this.editSaving.set(false);
      },
    });
  }

  // ── Delete ───────────────────────────────────────────────────────────────────

  deleteLog(id: string): void {
    this.confirmSvc.confirm({ title: this.translate.instant('logShift.confirm.deleteTitle'), message: this.translate.instant('logShift.confirm.deleteMsg'), confirmLabel: this.translate.instant('common.delete'), danger: true }).subscribe(ok => {
      if (!ok) return;
      this.deletingId.set(id);
      this.workLogSvc.delete(id).subscribe({
        next: () => { this.deletingId.set(null); this.loadHistory(); this.toast.success(this.translate.instant('logShift.toast.removed')); },
        error: () => { this.deletingId.set(null); this.toast.error(this.translate.instant('logShift.errDelete')); },
      });
    });
  }

}
