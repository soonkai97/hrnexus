import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DecimalPipe, UpperCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkLogService, WorkLogDto, LocationStore, ToastService, ConfirmService, AuthService, AmPmPipe, PickerOnlyDirective, PagedResult, todayLocalString } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';

type Mode = 'live' | 'manual';

function nowHHmm(): string {
  const d = new Date();
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
}

@Component({
  selector: 'app-log-shift',
  standalone: true,
  imports: [DecimalPipe, UpperCasePipe, FormsModule, TranslatePipe, AmPmPipe, PickerOnlyDirective, Button, Card, InputText, Select, Message, ProgressSpinner],
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
    return assigned.filter(r => r !== 'admin' && r !== 'manager');
  });

  readonly myRoleOptions = computed(() =>
    this.myRoles().map(r => ({ label: r.toUpperCase(), value: r }))
  );

  readonly locationOptions = computed(() =>
    this.locationStore.locations().map(l => ({ label: l.name, value: l.id }))
  );

  readonly locations = this.locationStore.locations;

  // Live form
  liveForm = {
    roleName:   '',
    locationId: '',
    startTime:  nowHHmm(),
    notes:      '',
  };

  // End shift
  endTime = nowHHmm();
  endingId = signal<string | null>(null);

  // Manual form
  manualForm = {
    roleName:   '',
    locationId: '',
    workDate:   todayLocalString(),
    startTime:  '',
    endTime:    '',
    notes:      '',
  };

  // Error
  formError = signal('');

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
    this.submitting.set(true);
    this.formError.set('');
    this.workLogSvc.start({
      roleName:   this.liveForm.roleName,
      locationId: this.liveForm.locationId,
      startTime:  this.liveForm.startTime,
      notes:      this.liveForm.notes || null,
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.toast.success('Shift started!');
        this.loadActive();
        this.liveForm = { roleName: '', locationId: '', startTime: nowHHmm(), notes: '' };
      },
      error: (e) => {
        this.formError.set(e?.error?.error ?? 'Failed to start shift.');
        this.submitting.set(false);
      },
    });
  }

  // ── Live: end shift ──────────────────────────────────────────────────────────

  openEndShift(id: string): void {
    this.endTime = nowHHmm();
    this.endingId.set(id);
  }

  cancelEnd(): void { this.endingId.set(null); }

  confirmEnd(): void {
    const id = this.endingId();
    if (!id) return;
    this.submitting.set(true);
    this.workLogSvc.end(id, this.endTime).subscribe({
      next: () => {
        this.submitting.set(false);
        this.endingId.set(null);
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
    if (!this.manualForm.roleName || !this.manualForm.locationId || !this.manualForm.workDate || !this.manualForm.startTime || !this.manualForm.endTime) {
      this.formError.set('Please fill in all fields.'); return;
    }
    // Validate time range (supports overnight; rejects identical start/end)
    const [sh, sm] = this.manualForm.startTime.split(':').map(Number);
    const [eh, em] = this.manualForm.endTime.split(':').map(Number);
    const startMins = sh * 60 + sm;
    const endMins   = eh * 60 + em;
    const durationMins = endMins > startMins ? endMins - startMins : endMins + 1440 - startMins;
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
      startTime:  this.manualForm.startTime,
      endTime:    this.manualForm.endTime,
      notes:      this.manualForm.notes || null,
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.toast.success('Shift logged!');
        this.manualForm = { roleName: '', locationId: '', workDate: todayLocalString(), startTime: '', endTime: '', notes: '' };
        this.loadHistory();
      },
      error: (e) => {
        this.formError.set(e?.error?.error ?? 'Failed to log shift.');
        this.submitting.set(false);
      },
    });
  }

  // ── Delete ───────────────────────────────────────────────────────────────────

  deleteLog(id: string): void {
    this.confirmSvc.confirm({ title: 'Delete Log', message: 'Remove this work log?', confirmLabel: 'Delete', danger: true }).subscribe(ok => {
      if (!ok) return;
      this.workLogSvc.delete(id).subscribe({
        next: () => { this.loadHistory(); this.toast.success('Log removed.'); },
        error: () => this.toast.error('Failed to delete.'),
      });
    });
  }

}
