import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LeaveService, ApplyLeaveRequest, LeaveRecord, LeaveBalance, ToastService, ConfirmService, toLocalDateString, nowRoundedToHalfHour, leaveHours, HmPipe, TrimInputDirective, CanComponentDeactivate, apiErrorMessage } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { Select } from 'primeng/select';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { SelectButton } from 'primeng/selectbutton';
import { Paginator } from 'primeng/paginator';

const LEAVE_TYPES = ['annual', 'sick', 'unpaid', 'emergency'] as const;
type StaffLeaveType = typeof LEAVE_TYPES[number];

@Component({
  selector: 'app-my-leave',
  standalone: true,
  imports: [DatePipe, FormsModule, HmPipe, TrimInputDirective,
    Button, Card, Tag, Select, InputText, Message, Skeleton, DatePicker, SelectButton, Paginator],
  templateUrl: './my-leave.component.html',
  styleUrl: './my-leave.component.scss',
})
export class MyLeaveComponent implements OnInit, CanComponentDeactivate {
  private readonly leaveSvc  = inject(LeaveService);
  private readonly toast     = inject(ToastService);
  private readonly confirm   = inject(ConfirmService);

  // PrimeNG 21 SelectButton does not support ng-template pTemplate="item".
  // Use optionLabel="label" (matching the pattern used by the profile language selector).
  get viewOptions() {
    return [
      { label: 'Apply for Leave', value: 'apply'   },
      { label: 'Leave History',    value: 'history' },
    ];
  }

  readonly leaveTypes = LEAVE_TYPES;
  private static readonly TYPE_LABELS: Record<string, string> = {
    annual: 'Annual Leave', sick: 'Sick Leave', unpaid: 'Unpaid Leave', emergency: 'Emergency Leave', 'no-show': 'No-Show',
  };
  private static readonly STATUS_LABELS: Record<string, string> = {
    pending: 'Pending', approved: 'Approved', rejected: 'Rejected', cancelled: 'Cancelled',
  };
  leaveTypeLabel(t: string): string { return MyLeaveComponent.TYPE_LABELS[t] ?? t; }
  statusLabel(s: string): string { return MyLeaveComponent.STATUS_LABELS[s] ?? s; }
  get leaveTypeOptions() {
    return LEAVE_TYPES.map(t => ({
      label: this.leaveTypeLabel(t),
      value: t,
    }));
  }
  readonly todayLocal = toLocalDateString(new Date());
  readonly todayIso   = toLocalDateString(new Date());
  /** Rounded "now" used as the time-picker default so the panel opens on a clean :00/:30. */
  defaultTime = nowRoundedToHalfHour();

  /** Item 8: calendar day-of-week header keys */
  readonly calDayLabels = [
    'Mon','Tue','Wed',
    'Thu','Fri','Sat','Sun',
  ];

  // NOTE: these are bound to the date pickers' [minDate]/[defaultDate]. They MUST return a
  // stable object reference across change-detection — PrimeNG's minDate/defaultDate setters
  // re-render (and defaultDate resets the visible month) whenever the value changes by
  // reference. Returning a fresh `new Date()` every CD made the prev/next arrows snap the
  // calendar back every cycle, so navigation appeared dead. Memoised by a value-key instead.
  private _minStartCache?: { key: string; date: Date };
  private _minEndCache?: { key: string; date: Date };

  /** Minimum selectable start date. Annual needs 3 days advance; others allow today. */
  get minStartDate(): Date {
    const todayStr = new Date().toISOString().slice(0, 10);
    const key = `${todayStr}:${this.form.leaveType === 'annual' ? 'annual' : 'std'}`;
    if (this._minStartCache?.key !== key) {
      const d = new Date();
      d.setHours(0, 0, 0, 0);
      if (this.form.leaveType === 'annual') d.setDate(d.getDate() + 3);
      this._minStartCache = { key, date: d };
    }
    return this._minStartCache.date;
  }

  /** Minimum end date — the chosen start date (or the min start date if none yet). */
  get minEndDate(): Date {
    const key = this.form.startDate
      || `min:${this.form.leaveType === 'annual' ? 'annual' : 'std'}`;
    if (this._minEndCache?.key !== key) {
      let date: Date;
      if (this.form.startDate) {
        date = new Date(this.form.startDate);
        date.setHours(0, 0, 0, 0);
      } else {
        date = this.minStartDate;
      }
      this._minEndCache = { key, date };
    }
    return this._minEndCache.date;
  }

  records      = signal<LeaveRecord[]>([]);
  balance      = signal<LeaveBalance | null>(null);
  loading      = signal(false);
  saving       = signal(false);
  formError    = signal('');
  view         = signal<'apply' | 'history'>('apply');
  cancellingId = signal<string | null>(null);

  filterLeaveType = signal('');
  filteredRecords = computed(() => {
    const type = this.filterLeaveType();
    return type ? this.records().filter(r => r.leaveType === type) : this.records();
  });

  // Client-side pagination of the history LIST view (the calendar view uses the full set).
  readonly leavePageRows = 10;
  readonly leaveFirst    = signal(0);
  readonly pagedRecords  = computed(() => {
    const all = this.filteredRecords();
    const start = this.leaveFirst() >= all.length ? 0 : this.leaveFirst();
    return all.slice(start, start + this.leavePageRows);
  });

  /** Change the type filter and jump back to the first page. */
  setLeaveFilter(value: string): void {
    this.filterLeaveType.set(value);
    this.leaveFirst.set(0);
  }

  get presetTypes() {
    return [
      { label: 'All',     value: '' },
      { label: 'Annual Leave',   value: 'annual' },
      { label: 'Sick Leave',     value: 'sick' },
      { label: 'Unpaid Leave',   value: 'unpaid' },
      { label: 'Emergency Leave',value: 'emergency' },
    ];
  }

  form: { leaveType: StaffLeaveType; startDate: string; endDate: string; startTime: Date | null; endTime: Date | null; notes: string } = {
    leaveType: 'annual', startDate: '', endDate: '', startTime: null, endTime: null, notes: '',
  };

  // ── Item 8: Calendar view state ───────────────────────────────────────────
  /** Toggle between list and calendar in History tab */
  historyView = signal<'list' | 'calendar'>('list');

  calYear  = signal(new Date().getFullYear());
  calMonth = signal(new Date().getMonth());

  calMonthName = computed(() =>
    new Date(this.calYear(), this.calMonth(), 1).toLocaleString('en-US', { month: 'long', year: 'numeric' }));

  calDays = computed(() => {
    const year  = this.calYear();
    const month = this.calMonth();
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const recs  = this.filteredRecords();
    return Array.from({ length: daysInMonth }, (_, i) => {
      const day  = i + 1;
      const date = new Date(Date.UTC(year, month, day));
      const iso  = date.toISOString().slice(0, 10);
      const leaves = recs.filter(r => {
        const from = r.startDate.slice(0, 10);
        const to   = r.endDate.slice(0, 10);
        return iso >= from && iso <= to;
      });
      return { day, iso, isWeekend: date.getUTCDay() === 0 || date.getUTCDay() === 6, leaves };
    });
  });

  leadingBlanks = computed(() => {
    const d = new Date(this.calYear(), this.calMonth(), 1).getDay();
    return Array(d === 0 ? 6 : d - 1).fill(0);
  });

  selectedCalDay = signal<string | null>(null);
  selectedCalDayLeaves = computed(() => {
    const iso = this.selectedCalDay();
    if (!iso) return [];
    return this.filteredRecords().filter(r => {
      const from = r.startDate.slice(0, 10);
      const to   = r.endDate.slice(0, 10);
      return iso >= from && iso <= to;
    });
  });

  prevCalMonth(): void {
    if (this.calMonth() === 0) { this.calMonth.set(11); this.calYear.update(y => y - 1); }
    else                         this.calMonth.update(m => m - 1);
    this.selectedCalDay.set(null);
  }

  nextCalMonth(): void {
    if (this.calMonth() === 11) { this.calMonth.set(0); this.calYear.update(y => y + 1); }
    else                          this.calMonth.update(m => m + 1);
    this.selectedCalDay.set(null);
  }

  selectCalDay(iso: string): void {
    this.selectedCalDay.set(this.selectedCalDay() === iso ? null : iso);
  }

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
  }

  /** Working-day hours (Mon-Fri, 8h) for the chosen dates/times, or null when invalid. Same rule as the server. */
  private get previewHours(): number | null {
    const { startDate, endDate, startTime, endTime } = this.form;
    if (!startDate || !startTime || !endDate || !endTime) return null;
    return leaveHours(startDate, endDate, this.toTimeStr(startTime), this.toTimeStr(endTime));
  }

  /** Live preview of the leave hours. Returns 0 if any field is missing or the request is invalid. */
  get calcHours(): number {
    return this.previewHours ?? 0;
  }

  /** Required fields present — gates the Apply button (consistent with every other form). */
  get canSubmit(): boolean {
    return !!this.form.startDate && !!this.form.startTime
        && !!this.form.endDate   && !!this.form.endTime;
  }

  ngOnInit(): void {
    this.load();
    this.leaveSvc.getMyBalance().subscribe({ next: b => this.balance.set(b), error: () => {} });
  }

  load(): void {
    this.loading.set(true);
    this.leaveSvc.getMine().subscribe({
      next:  r  => { this.records.set(r); this.loading.set(false); },
      error: () => { this.toast.error('Failed to load leave records.'); this.loading.set(false); },
    });
  }

  submit(): void {
    this.formError.set('');
    if (!this.form.startDate || !this.form.startTime || !this.form.endDate || !this.form.endTime) {
      this.formError.set('Please select start date, end date, and times.'); return;
    }

    const hours = this.previewHours;
    if (hours === null) {
      this.formError.set('Choose at least one working day (Mon-Fri) and valid times.'); return;
    }

    const maxFuture = new Date();
    maxFuture.setFullYear(maxFuture.getFullYear() + 1);
    const startDt = new Date(this.form.startDate);
    startDt.setHours(this.form.startTime.getHours(), this.form.startTime.getMinutes(), 0, 0);
    if (startDt > maxFuture) {
      this.formError.set('Leave cannot be requested more than 1 year in advance.'); return;
    }

    if (this.saving()) return;
    this.saving.set(true);
    const req: ApplyLeaveRequest = {
      leaveType: this.form.leaveType,
      startDate: this.form.startDate,
      endDate:   this.form.endDate,
      startTime: this.toTimeStr(this.form.startTime),
      endTime:   this.toTimeStr(this.form.endTime),
      hours,
      notes:     this.form.notes || null,
    };
    this.leaveSvc.apply(req).subscribe({
      next: () => {
        this.saving.set(false);
        this.formError.set('');
        this.form = { leaveType: 'annual', startDate: '', endDate: '', startTime: null, endTime: null, notes: '' };
        this.load();
        this.leaveSvc.getMyBalance().subscribe({ next: b => this.balance.set(b), error: () => {} });
        this.toast.success('Leave request submitted.');
        this.view.set('history');
      },
      error: (err) => {
        this.formError.set(err?.error?.error ?? 'Submission failed. Please try again.');
        this.saving.set(false);
      },
    });
  }

  hasUnsavedChanges(): boolean {
    // Consider the Apply form dirty if the user has touched any field beyond the default leave type.
    return this.view() === 'apply' && (
      !!this.form.startDate ||
      !!this.form.endDate   ||
      this.form.startTime !== null ||
      this.form.endTime   !== null ||
      !!this.form.notes.trim()
    );
  }

  onStartDateChange(date: string): void {
    if (!this.form.endDate || this.form.endDate < date) {
      this.form.endDate = date;
    }
  }

  cancel(r: LeaveRecord): void {
    const isApproved = r.status === 'approved';
    this.confirm.confirm({
      title:        isApproved ? 'Withdraw Approved Leave' : 'Cancel Leave Request',
      message:      isApproved
        ? 'This leave has already been approved. Withdrawing it will remove the approval and restore your balance. Are you sure?'
        : 'Are you sure you want to withdraw this leave request? This cannot be undone.',
      confirmLabel: 'Yes, Withdraw',
      danger:       true,
    }).subscribe((confirmed: boolean) => {
      if (!confirmed) return;
      this.cancellingId.set(r.id);
      this.leaveSvc.cancel(r.id).subscribe({
        next:  () => { this.cancellingId.set(null); this.load(); this.toast.success('Leave request withdrawn.'); },
        error: (e: unknown) => {
          this.cancellingId.set(null);
          const msg: string = apiErrorMessage(e) ?? '';
          this.toast.error(
            msg.toLowerCase().includes('ended') || msg.toLowerCase().includes('cancel')
              ? 'This leave has already ended and cannot be cancelled. Contact your manager.'
              : 'Failed to cancel leave.'
          );
        },
      });
    });
  }
}
