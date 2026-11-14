import { Component, OnInit, signal, computed, effect, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LeaveService, ApplyLeaveRequest, LeaveRecord, LeaveBalance, LeaveTypeBalance, ToastService, ConfirmService, toLocalDateString, nowRoundedToHalfHour, leaveHours, HmPipe, TrimInputDirective, CanComponentDeactivate, apiErrorMessage, CompanyPolicyService, LeaveTypeService } from 'shared-lib';
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
  private readonly policySvc = inject(CompanyPolicyService);
  protected readonly typeSvc = inject(LeaveTypeService);
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

  /** The types staff can apply for (active, not recorded by an administrator): from the company's catalogue. */
  readonly leaveTypes = computed(() => this.typeSvc.applicable().map(t => t.key));
  private static readonly STATUS_LABELS: Record<string, string> = {
    pending: 'Pending', approved: 'Approved', rejected: 'Rejected', cancelled: 'Cancelled',
  };
  leaveTypeLabel(t: string): string { return this.typeSvc.label(t); }
  statusLabel(s: string): string { return MyLeaveComponent.STATUS_LABELS[s] ?? s; }
  get leaveTypeOptions() {
    return this.typeSvc.applicable().map(t => ({ label: t.label, value: t.key }));
  }

  /** The chosen type's definition: its notice, limit, day counting and approval rule drive the form. */
  get selectedType() { return this.typeSvc.find(this.form.leaveType); }

  /** What the form tells the person about the chosen type, from its rules (never from its name). */
  get typeHints(): string[] {
    const t = this.selectedType;
    if (!t) return [];
    const hints: string[] = [];
    const notice = this.typeSvc.noticeDays(t, this.policySvc.policy().annualLeaveAdvanceDays);
    if (notice !== null && notice > 0) hints.push(`${t.label} leave must be applied at least ${notice} days in advance.`);
    if (t.autoApprove) hints.push(`${t.label} leave is approved straight away. Use it for urgent or short-notice requests.`);
    else if (t.maxDaysPerRequest !== null) hints.push(`The longest single request is ${t.maxDaysPerRequest} ${t.countsCalendarDays ? 'calendar ' : ''}days; a manager approves it.`);
    if (t.countsCalendarDays) hints.push('Every day counts, weekends included.');
    return hints;
  }

  /** Balances for types that have no fixed card: an own allowance (hospitalisation) or other leave already taken. */
  readonly extraBalances = computed<LeaveTypeBalance[]>(() =>
    (this.balance()?.types ?? []).filter(t =>
      (t.pool === 'own' && (t.active || t.used > 0)) || (t.pool === 'none' && !this.isPayDeducting(t.key) && t.used > 0)));
  private isPayDeducting(key: string): boolean { return this.typeSvc.find(key)?.deductsPay ?? false; }
  /** The tag severity for a type, from its catalogue colour. */
  typeSeverity(key: string) { return this.typeSvc.severity(key); }
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

  /** Days of notice the chosen type needs (null = none), from the type's rules and the company policy. */
  private get noticeDays(): number | null {
    const t = this.selectedType;
    return t ? this.typeSvc.noticeDays(t, this.policySvc.policy().annualLeaveAdvanceDays) : null;
  }

  /** Minimum selectable start date: today plus the chosen type's notice (none for most types). */
  get minStartDate(): Date {
    const todayStr = new Date().toISOString().slice(0, 10);
    const notice = this.noticeDays;
    const key = `${todayStr}:${notice ?? 'std'}`;
    if (this._minStartCache?.key !== key) {
      const d = new Date();
      d.setHours(0, 0, 0, 0);
      if (notice) d.setDate(d.getDate() + notice);
      this._minStartCache = { key, date: d };
    }
    return this._minStartCache.date;
  }

  /** Minimum end date — the chosen start date (or the min start date if none yet). */
  get minEndDate(): Date {
    const key = this.form.startDate
      || `min:${this.noticeDays ?? 'std'}`;
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

  /** The history filter chips: the types staff can use, plus any type that already has a record. */
  get presetTypes() {
    const used = new Set(this.records().map(r => r.leaveType));
    return [
      { label: 'All', value: '' },
      ...this.typeSvc.types()
        .filter(t => (t.active && !t.adminOnly) || used.has(t.key))
        .map(t => ({ label: t.label, value: t.key })),
    ];
  }

  form: { leaveType: string; startDate: string; endDate: string; startTime: Date | null; endTime: Date | null; notes: string } = {
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
    const schedule = this.policySvc.schedule();
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
      return { day, iso, isNonWorking: !schedule.days.includes(date.getUTCDay() || 7), leaves };
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

  /** Working-day hours (the company's working week) for the chosen dates/times, or null when invalid. Same rule as the server. */
  private get previewHours(): number | null {
    const { startDate, endDate, startTime, endTime } = this.form;
    if (!startDate || !startTime || !endDate || !endTime) return null;
    return leaveHours(startDate, endDate, this.toTimeStr(startTime), this.toTimeStr(endTime),
      this.typeSvc.scheduleFor(this.form.leaveType, this.policySvc.schedule()));
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

  constructor() {
    // The catalogue may load after the page opens, or turn the chosen type off: always stay on a type staff can apply for.
    effect(() => {
      const keys = this.leaveTypes();
      const first = keys[0];
      if (first && !keys.includes(this.form.leaveType)) this.form.leaveType = first;
    });
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
      this.formError.set('Choose at least one working day and valid times.'); return;
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
        this.form = { leaveType: this.leaveTypes()[0] ?? 'annual', startDate: '', endDate: '', startTime: null, endTime: null, notes: '' };
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
