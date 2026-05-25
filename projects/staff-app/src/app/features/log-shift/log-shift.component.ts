import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DecimalPipe, UpperCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkLogService, WorkLogDto, UpdateWorkLogRequest, LocationStore, ToastService, ConfirmService, AuthService, AmPmPipe, PagedResult, todayLocalString } from 'shared-lib';
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
  imports: [DecimalPipe, UpperCasePipe, FormsModule, TranslatePipe, AmPmPipe, Button, Card, Tag, InputText, Select, Message, Skeleton, DatePicker, Tabs, TabList, Tab, Paginator],
  templateUrl: './log-shift.component.html',
  styleUrl: './log-shift.component.scss',
})
export class LogShiftComponent implements OnInit {
  private readonly workLogSvc    = inject(WorkLogService);
  private readonly locationStore = inject(LocationStore);
  private readonly toast         = inject(ToastService);
  private readonly confirmSvc    = inject(ConfirmService);
  readonly auth                  = inject(AuthService);

  mode = signal<Mode>('live');
  view = signal<'log' | 'history'>('log');

  readonly viewOptions = [
    { label: 'Log Shift', value: 'log'     },
    { label: 'History',   value: 'history' },
  ];
  readonly modeOptions = [
    { label: 'Start Now',     value: 'live'   },
    { label: 'Manual Entry',  value: 'manual' },
  ];

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
    return assigned.filter(r => r !== 'ADMIN');
  });

  readonly myRoleOptions = computed(() =>
    this.myRoles().map(r => ({ label: r, value: r }))
  );

  readonly locationOptions = computed(() =>
    this.locationStore.locations().map(l => ({ label: l.name, value: l.id }))
  );

  readonly locations = this.locationStore.locations;

  // Live form
  liveForm = {
    roleName:   '',
    locationId: '',
    startTime:  nowHHmm(), // kept for API compat; template uses liveFormStartTime
    notes:      '',
  };
  liveFormStartTime: Date | null = new Date(); // UI binding for live start time

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

  get isOvernightManual(): boolean {
    if (!this.manualFormStartTime || !this.manualFormEndTime) return false;
    const sm = this.manualFormStartTime.getHours() * 60 + this.manualFormStartTime.getMinutes();
    const em = this.manualFormEndTime.getHours() * 60 + this.manualFormEndTime.getMinutes();
    return em < sm;
  }

  // Error
  formError = signal('');

  deletingId = signal<string | null>(null);

  // ── Edit ─────────────────────────────────────────────────────────────────────
  editingLogId = signal<string | null>(null);
  editSaving   = signal(false);
  editError    = signal('');
  editForm: UpdateWorkLogRequest = { roleName: '', locationId: '', workDate: '', startTime: '', endTime: '', notes: '' };
  editFormStartTime: Date | null = null; // UI binding
  editFormEndTime:   Date | null = null; // UI binding

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  private parseTime(t: string): Date | null {
    if (!t) return null;
    const [h, m] = t.split(':').map(Number);
    if (isNaN(h) || isNaN(m)) return null;
    const d = new Date();
    d.setHours(h, m, 0, 0);
    return d;
  }

  ngOnInit(): void {
    this.locationStore.load();
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
        const activeCount = r.items.filter((l: any) => l.isActive).length;
        this.history.set(r.items.filter((l: any) => !l.isActive));
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
      this.formError.set('Please select a role and location.'); return;
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
        this.toast.success('Shift started!');
        this.loadActive();
        this.liveForm = { roleName: '', locationId: '', startTime: nowHHmm(), notes: '' };
        this.liveFormStartTime = new Date();
      },
      error: (e) => {
        this.formError.set(e?.error?.error ?? 'Failed to start shift.');
        this.submitting.set(false);
      },
    });
  }

  // ── Live: end shift ──────────────────────────────────────────────────────────

  openEndShift(id: string): void {
    this.endTimeDate = new Date();
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
        this.toast.success('Shift ended!');
        this.activeLog.set(null);
        this.loadHistory();
      },
      error: (e) => {
        this.toast.error(e?.error?.error ?? 'Failed to end shift.');
        this.submitting.set(false);
      },
    });
  }

  // ── Manual entry ─────────────────────────────────────────────────────────────

  submitManual(): void {
    if (!this.manualForm.roleName || !this.manualForm.locationId || !this.manualForm.workDate || !this.manualFormStartTime || !this.manualFormEndTime) {
      this.formError.set('Please fill in all fields.'); return;
    }
    // Convert Date → HH:mm for validation and API
    const startTime = this.toTimeStr(this.manualFormStartTime);
    const endTime   = this.toTimeStr(this.manualFormEndTime);
    const [sh, sm] = startTime.split(':').map(Number);
    const [eh, em] = endTime.split(':').map(Number);
    const startMins = sh * 60 + sm;
    const endMins   = eh * 60 + em;
    // >= so same-time gives 0 (not 1440); overnight gives [1, 1439]
    const durationMins = endMins >= startMins ? endMins - startMins : endMins + 1440 - startMins;
    if (durationMins === 0) {
      this.formError.set('End time must be different from start time.'); return;
    }
    if (durationMins > 1440) {
      this.formError.set('A single shift cannot exceed 24 hours.'); return;
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
        this.toast.success('Shift logged!');
        this.manualForm = { roleName: '', locationId: '', workDate: todayLocalString(), startTime: '', endTime: '', notes: '' };
        this.manualFormStartTime = null;
        this.manualFormEndTime   = null;
        this.loadHistory();
      },
      error: (e) => {
        this.formError.set(e?.error?.error ?? 'Failed to log shift.');
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
      this.editError.set('Please fill in all required fields.'); return;
    }
    const startTime = this.toTimeStr(this.editFormStartTime);
    const endTime   = this.toTimeStr(this.editFormEndTime);
    const [sh, sm] = startTime.split(':').map(Number);
    const [eh, em] = endTime.split(':').map(Number);
    const durationMins = (eh * 60 + em) >= (sh * 60 + sm)
      ? (eh * 60 + em) - (sh * 60 + sm)
      : (eh * 60 + em) + 1440 - (sh * 60 + sm);
    if (durationMins === 0) { this.editError.set('End time must differ from start time.'); return; }
    if (durationMins > 1440) { this.editError.set('A single shift cannot exceed 24 hours.'); return; }

    this.editSaving.set(true);
    this.editError.set('');
    this.workLogSvc.update(id, { ...this.editForm, startTime, endTime, notes: this.editForm.notes || null }).subscribe({
      next: () => {
        this.editSaving.set(false);
        this.editingLogId.set(null);
        this.editFormStartTime = null;
        this.editFormEndTime   = null;
        this.toast.success('Shift updated.');
        this.loadHistory();
      },
      error: (e) => {
        this.editError.set(e?.error?.error ?? 'Failed to update shift.');
        this.editSaving.set(false);
      },
    });
  }

  // ── Delete ───────────────────────────────────────────────────────────────────

  deleteLog(id: string): void {
    this.confirmSvc.confirm({ title: 'Delete Shift Log', message: 'Are you sure you want to remove this shift record? This cannot be undone.', confirmLabel: 'Delete', danger: true }).subscribe(ok => {
      if (!ok) return;
      this.deletingId.set(id);
      this.workLogSvc.delete(id).subscribe({
        next: () => { this.deletingId.set(null); this.loadHistory(); this.toast.success('Log removed.'); },
        error: () => { this.deletingId.set(null); this.toast.error('Failed to delete.'); },
      });
    });
  }

}
