import { Component, OnInit, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LeaveService, ApplyLeaveRequest, LeaveRecord, LeaveBalance, ToastService, ConfirmService, toLocalDateString } from 'shared-lib';
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
  imports: [FormsModule, TranslatePipe, Button, Card, Tag, Select, InputText, Message, Skeleton, DatePicker, SelectButton],
  templateUrl: './my-leave.component.html',
  styleUrl: './my-leave.component.scss',
})
export class MyLeaveComponent implements OnInit {
  private readonly leaveSvc = inject(LeaveService);
  private readonly toast    = inject(ToastService);
  private readonly confirm  = inject(ConfirmService);

  readonly viewOptions = [
    { labelKey: 'leave.applyLeave', value: 'apply'   },
    { labelKey: 'leave.history',    value: 'history' },
  ];

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

  form: { leaveType: StaffLeaveType; startDate: string; endDate: string; notes: string } = {
    leaveType: 'annual', startDate: '', endDate: '', notes: '',
  };

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
    if (!this.form.startDate || !this.form.endDate) {
      this.formError.set('Please select start and end dates.'); return;
    }
    if (this.form.startDate > this.form.endDate) {
      this.formError.set('End date must be on or after start date.'); return;
    }

    const start    = new Date(this.form.startDate);
    const end      = new Date(this.form.endDate);
    const duration = (end.getTime() - start.getTime()) / (1000 * 60 * 60 * 24) + 1;
    if (duration > 60) {
      this.formError.set('Leave duration cannot exceed 60 days in a single request.'); return;
    }

    const maxFuture = new Date();
    maxFuture.setFullYear(maxFuture.getFullYear() + 1);
    if (start > maxFuture) {
      this.formError.set('Leave cannot be requested more than 1 year in advance.'); return;
    }

    if (this.saving()) return;
    this.saving.set(true);
    const req: ApplyLeaveRequest = {
      leaveType: this.form.leaveType,
      startDate: this.form.startDate,
      endDate:   this.form.endDate,
      notes:     this.form.notes || null,
    };
    this.leaveSvc.apply(req).subscribe({
      next: () => {
        this.saving.set(false);
        this.formError.set('');
        this.form = { leaveType: 'annual', startDate: '', endDate: '', notes: '' };
        this.load();
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
