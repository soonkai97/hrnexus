import { Component, OnInit, signal, inject } from '@angular/core';
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

const LEAVE_TYPES = ['sick', 'annual', 'unpaid'] as const;
type StaffLeaveType = typeof LEAVE_TYPES[number];

@Component({
  selector: 'app-my-leave',
  standalone: true,
  imports: [FormsModule, TranslatePipe, AmPmPipe, Button, Card, Tag, Select, InputText, Message, Skeleton, DatePicker, SelectButton],
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
    label: t.charAt(0).toUpperCase() + t.slice(1),
    value: t,
  }));
  readonly todayLocal = toLocalDateString(new Date());


  records     = signal<LeaveRecord[]>([]);
  balance     = signal<LeaveBalance | null>(null);
  loading     = signal(false);
  saving      = signal(false);
  formError   = signal('');
  view        = signal<'apply' | 'history'>('apply');

  form: { leaveType: StaffLeaveType; startDate: string; endDate: string; startTime: string; endTime: string; notes: string } = {
    leaveType: 'annual', startDate: '', endDate: '', startTime: '', endTime: '', notes: '',
  };

  /** Auto-calculated hours from date+time range. Returns 0 if any field is missing. */
  get calcHours(): number {
    const { startDate, startTime, endDate, endTime } = this.form;
    if (!startDate || !startTime || !endDate || !endTime) return 0;
    const start = new Date(`${startDate}T${startTime}`);
    const end   = new Date(`${endDate}T${endTime}`);
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
    if (new Date(`${this.form.startDate}T${this.form.startTime}`) > maxFuture) {
      this.formError.set('Leave cannot be requested more than 1 year in advance.'); return;
    }

    if (this.saving()) return;
    this.saving.set(true);
    const req: ApplyLeaveRequest = {
      leaveType: this.form.leaveType,
      startDate: this.form.startDate,
      endDate:   this.form.endDate,
      startTime: this.form.startTime,
      endTime:   this.form.endTime,
      hours,
      notes:     this.form.notes || null,
    };
    this.leaveSvc.apply(req).subscribe({
      next: () => {
        this.saving.set(false);
        this.formError.set('');
        this.form = { leaveType: 'annual', startDate: '', endDate: '', startTime: '', endTime: '', notes: '' };
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
