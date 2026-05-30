import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LeaveService, ApplyLeaveRequest, LeaveRecord, LeaveBalance, ToastService, ConfirmService, toLocalDateString, AmPmPipe, TrimInputDirective, CanComponentDeactivate, apiErrorMessage } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { Select } from 'primeng/select';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { SelectButton } from 'primeng/selectbutton';

const LEAVE_TYPES = ['annual', 'sick', 'unpaid', 'emergency'] as const;
type StaffLeaveType = typeof LEAVE_TYPES[number];

@Component({
  selector: 'app-my-leave',
  standalone: true,
  imports: [DatePipe, FormsModule, TranslatePipe, AmPmPipe, TrimInputDirective,
    Button, Card, Tag, Select, InputText, Message, Skeleton, DatePicker, SelectButton],
  templateUrl: './my-leave.component.html',
  styleUrl: './my-leave.component.scss',
})
export class MyLeaveComponent implements OnInit, CanComponentDeactivate {
  private readonly leaveSvc  = inject(LeaveService);
  private readonly toast     = inject(ToastService);
  private readonly confirm   = inject(ConfirmService);
  private readonly translate = inject(TranslateService);

  // PrimeNG 21 SelectButton does not support ng-template pTemplate="item".
  // Use optionLabel="label" (matching the pattern used by the profile language selector).
  get viewOptions() {
    return [
      { label: this.translate.instant('leave.applyLeave'), value: 'apply'   },
      { label: this.translate.instant('leave.history'),    value: 'history' },
    ];
  }

  readonly leaveTypes = LEAVE_TYPES;
  get leaveTypeOptions() {
    return LEAVE_TYPES.map(t => ({
      label: this.translate.instant('leave.type.' + t),
      value: t,
    }));
  }
  readonly todayLocal = toLocalDateString(new Date());
  readonly todayIso   = toLocalDateString(new Date());

  /** Item 8: calendar day-of-week header keys */
  readonly calDayKeys = [
    'leave.calDayMon','leave.calDayTue','leave.calDayWed',
    'leave.calDayThu','leave.calDayFri','leave.calDaySat','leave.calDaySun',
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

  get presetTypes() {
    return [
      { label: this.translate.instant('leave.filterAll'),     value: '' },
      { label: this.translate.instant('leave.type.annual'),   value: 'annual' },
      { label: this.translate.instant('leave.type.sick'),     value: 'sick' },
      { label: this.translate.instant('leave.type.unpaid'),   value: 'unpaid' },
      { label: this.translate.instant('leave.type.emergency'),value: 'emergency' },
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

  /** Auto-calculated hours from date+time range. Returns 0 if any field is missing. */
  get calcHours(): number {
    const { startDate, endDate, startTime, endTime } = this.form;
    if (!startDate || !startTime || !endDate || !endTime) return 0;
    const start = new Date(startDate);
    start.setHours(startTime.getHours(), startTime.getMinutes(), 0, 0);
    const end = new Date(endDate);
    end.setHours(endTime.getHours(), endTime.getMinutes(), 0, 0);
    const h = (end.getTime() - start.getTime()) / (1000 * 60 * 60);
    return Math.round(h * 10) / 10;
  }

  ngOnInit(): void {
    this.load();
    this.leaveSvc.getMyBalance().subscribe({ next: b => this.balance.set(b), error: () => {} });
  }

  load(): void {
    this.loading.set(true);
    this.leaveSvc.getMine().subscribe({
      next:  r  => { this.records.set(r); this.loading.set(false); },
      error: () => { this.toast.error(this.translate.instant('leave.errLoad')); this.loading.set(false); },
    });
  }

  submit(): void {
    this.formError.set('');
    if (!this.form.startDate || !this.form.startTime || !this.form.endDate || !this.form.endTime) {
      this.formError.set(this.translate.instant('leave.errSelectDates')); return;
    }

    const hours = this.calcHours;
    if (hours <= 0) {
      this.formError.set(this.translate.instant('leave.errEndAfterStart')); return;
    }
    if (hours > 720) {
      this.formError.set(this.translate.instant('leave.errMaxDuration')); return;
    }

    const maxFuture = new Date();
    maxFuture.setFullYear(maxFuture.getFullYear() + 1);
    const startDt = new Date(this.form.startDate);
    startDt.setHours(this.form.startTime.getHours(), this.form.startTime.getMinutes(), 0, 0);
    if (startDt > maxFuture) {
      this.formError.set(this.translate.instant('leave.errMaxFuture')); return;
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
        this.toast.success(this.translate.instant('leave.toast.submitted'));
        this.view.set('history');
      },
      error: (err) => {
        this.formError.set(err?.error?.error ?? this.translate.instant('leave.errSubmit'));
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
      title:        isApproved ? this.translate.instant('leave.confirm.withdrawTitle') : this.translate.instant('leave.confirm.cancelTitle'),
      message:      isApproved
        ? this.translate.instant('leave.confirm.withdrawMsg')
        : this.translate.instant('leave.confirm.cancelMsg'),
      confirmLabel: this.translate.instant('leave.confirm.yesWithdraw'),
      danger:       true,
    }).subscribe((confirmed: boolean) => {
      if (!confirmed) return;
      this.cancellingId.set(r.id);
      this.leaveSvc.cancel(r.id).subscribe({
        next:  () => { this.cancellingId.set(null); this.load(); this.toast.success(this.translate.instant('leave.toast.withdrawn')); },
        error: (e: unknown) => {
          this.cancellingId.set(null);
          const msg: string = apiErrorMessage(e) ?? '';
          this.toast.error(
            msg.toLowerCase().includes('ended') || msg.toLowerCase().includes('cancel')
              ? this.translate.instant('leave.errAlreadyEnded')
              : this.translate.instant('leave.errCancel')
          );
        },
      });
    });
  }
}
