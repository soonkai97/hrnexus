import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LeaveService, ApplyLeaveRequest, LeaveRecord, LeaveBalance, ToastService, ConfirmService, toLocalDateString, AmPmPipe } from 'shared-lib';
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
  imports: [DatePipe, TitleCasePipe, FormsModule, TranslatePipe, AmPmPipe,
    Button, Card, Tag, Select, InputText, Message, Skeleton, DatePicker, SelectButton],
  templateUrl: './my-leave.component.html',
  styleUrl: './my-leave.component.scss',
})
export class MyLeaveComponent implements OnInit {
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
  readonly leaveTypeOptions = LEAVE_TYPES.map(t => ({
    label: t === 'annual' ? 'Annual' : t === 'sick' ? 'Sick' : t === 'unpaid' ? 'Unpaid' : 'Emergency',
    value: t,
  }));
  readonly todayLocal = toLocalDateString(new Date());
  readonly todayIso   = toLocalDateString(new Date());

  /** Item 8: calendar day-of-week header keys */
  readonly calDayKeys = [
    'leave.calDayMon','leave.calDayTue','leave.calDayWed',
    'leave.calDayThu','leave.calDayFri','leave.calDaySat','leave.calDaySun',
  ];

  /** Item 3: Minimum selectable start date. Annual needs 3 days advance; others allow today. */
  get minStartDate(): Date {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    if (this.form.leaveType === 'annual') {
      const d = new Date(today);
      d.setDate(d.getDate() + 3);
      return d;
    }
    return today;
  }

  /** Item 3: Minimum end date — same as start date (or today if not yet set). */
  get minEndDate(): Date {
    if (this.form.startDate) {
      const d = new Date(this.form.startDate);
      d.setHours(0, 0, 0, 0);
      return d;
    }
    return this.minStartDate;
  }

  records     = signal<LeaveRecord[]>([]);
  balance     = signal<LeaveBalance | null>(null);
  loading     = signal(false);
  saving      = signal(false);
  formError   = signal('');
  view        = signal<'apply' | 'history'>('apply');

  filterLeaveType = signal('');
  filteredRecords = computed(() => {
    const type = this.filterLeaveType();
    return type ? this.records().filter(r => r.leaveType === type) : this.records();
  });

  readonly presetTypes = [
    { label: 'All',       value: '' },
    { label: 'Annual',    value: 'annual' },
    { label: 'Sick',      value: 'sick' },
    { label: 'Unpaid',    value: 'unpaid' },
    { label: 'Emergency', value: 'emergency' },
  ];

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

  /** Auto-calculated hours from date+time range. Returns 0 if any field is missing. */
  get calcHours(): number {
    const { startDate, endDate } = this.form;
    const st = this.form.startTime;
    const et = this.form.endTime;
    if (!startDate || !st || !endDate || !et) return 0;
    const start = new Date(startDate);
    start.setHours(st.getHours(), st.getMinutes(), 0, 0);
    const end = new Date(endDate);
    end.setHours(et.getHours(), et.getMinutes(), 0, 0);
    const h = (end.getTime() - start.getTime()) / (1000 * 60 * 60);
    return Math.round(h * 10) / 10;
  }

  private toTimeStr(d: Date): string {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
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

    const hours = this.calcHours;
    if (hours <= 0) {
      this.formError.set('End date/time must be after start date/time.'); return;
    }
    if (hours > 720) {
      this.formError.set('Leave duration cannot exceed 720 hours (30 days) in a single request.'); return;
    }

    const maxFuture = new Date();
    maxFuture.setFullYear(maxFuture.getFullYear() + 1);
    const startDt = new Date(this.form.startDate);
    startDt.setHours(this.form.startTime!.getHours(), this.form.startTime!.getMinutes(), 0, 0);
    if (startDt > maxFuture) {
      this.formError.set('Leave cannot be requested more than 1 year in advance.'); return;
    }

    if (this.saving()) return;
    this.saving.set(true);
    const req: ApplyLeaveRequest = {
      leaveType: this.form.leaveType,
      startDate: this.form.startDate,
      endDate:   this.form.endDate,
      startTime: this.toTimeStr(this.form.startTime!),
      endTime:   this.toTimeStr(this.form.endTime!),
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
      this.leaveSvc.cancel(r.id).subscribe({
        next:  () => { this.load(); this.toast.success('Leave request withdrawn.'); },
        error: (e: any) => {
          const msg: string = e?.error?.error ?? e?.error?.message ?? '';
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
