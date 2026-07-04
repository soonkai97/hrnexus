import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CashAdvanceService, CashAdvanceDto, StaffStore, ToastService, ConfirmService,
         CanComponentDeactivate, apiErrorMessage } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { SelectButton } from 'primeng/selectbutton';
import { TooltipModule } from 'primeng/tooltip';
import { PrimeTemplate } from 'primeng/api';

type Filter = 'all' | 'outstanding' | 'settled';

@Component({
  selector: 'app-cash-advances',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, Button, InputNumber, InputText, Select,
    DatePicker, TableModule, Tag, Card, Dialog, SelectButton, TooltipModule, PrimeTemplate],
  templateUrl: './cash-advances.component.html',
  styleUrl: './cash-advances.component.scss',
})
export class CashAdvancesComponent implements OnInit, CanComponentDeactivate {
  private readonly svc        = inject(CashAdvanceService);
  private readonly staffStore = inject(StaffStore);
  private readonly toast      = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly translate  = inject(TranslateService);

  advances = signal<CashAdvanceDto[]>([]);
  loading  = signal(false);
  filter   = signal<Filter>('all');
  busyId   = signal<string | null>(null);

  get filterOptions() {
    return [
      { label: this.translate.instant('cashAdvances.filterAll'),         value: 'all' },
      { label: this.translate.instant('cashAdvances.filterOutstanding'), value: 'outstanding' },
      { label: this.translate.instant('cashAdvances.filterSettled'),     value: 'settled' },
    ];
  }

  readonly staffOptions = computed(() =>
    this.staffStore.staff()
      .filter(s => s.isActive)
      .map(s => ({ label: `${s.staffId} — ${s.fullName}`, value: s.id })));

  readonly totalOutstanding = computed(() =>
    this.advances().filter(a => a.status === 'outstanding').reduce((sum, a) => sum + a.amount, 0));

  // Record form
  showForm = signal(false);
  saving   = signal(false);
  form: { employeeId: string; amount: number | null; advanceDate: Date; reason: string } =
    { employeeId: '', amount: null, advanceDate: new Date(), reason: '' };

  readonly todayDate = new Date();

  ngOnInit(): void {
    this.staffStore.loadStaff('', 1, 100);
    this.load();
  }

  hasUnsavedChanges(): boolean {
    return this.showForm() && (!!this.form.employeeId || !!this.form.amount);
  }

  load(): void {
    this.loading.set(true);
    const f = this.filter();
    this.svc.list(f === 'all' ? undefined : f).subscribe({
      next: list => { this.advances.set(list); this.loading.set(false); },
      error: () => { this.toast.error(this.translate.instant('cashAdvances.loadFailed')); this.loading.set(false); },
    });
  }

  setFilter(f: Filter): void { this.filter.set(f); this.load(); }

  openAdd(): void {
    this.form = { employeeId: '', amount: null, advanceDate: new Date(), reason: '' };
    this.showForm.set(true);
  }
  cancel(): void { this.showForm.set(false); }

  get isFormValid(): boolean {
    return !!this.form.employeeId && this.form.amount != null && this.form.amount > 0 && !!this.form.advanceDate;
  }

  save(): void {
    if (!this.isFormValid) return;
    this.saving.set(true);
    this.svc.create({
      employeeId:  this.form.employeeId,
      amount:      this.form.amount!,
      advanceDate: this.toYmd(this.form.advanceDate),
      reason:      this.form.reason.trim() || null,
    }).subscribe({
      next:  () => { this.saving.set(false); this.showForm.set(false); this.toast.success(this.translate.instant('cashAdvances.recorded')); this.load(); },
      error: (e) => { this.saving.set(false); this.toast.error(apiErrorMessage(e) ?? this.translate.instant('cashAdvances.saveFailed')); },
    });
  }

  toggleSettled(a: CashAdvanceDto): void {
    if (this.busyId()) return;
    const settled = a.status !== 'settled';
    // Confirm first — this flips a financial record's status, so guard against a mis-tap
    // (consistent with the other cash-advance action). Not a "danger" red button — it's reversible.
    this.confirmSvc.confirm({
      title:        this.translate.instant(settled ? 'cashAdvances.confirmSettleTitle'   : 'cashAdvances.confirmReopenTitle'),
      message:      this.translate.instant(settled ? 'cashAdvances.confirmSettleMessage' : 'cashAdvances.confirmReopenMessage'),
      confirmLabel: this.translate.instant(settled ? 'cashAdvances.markSettled'          : 'cashAdvances.reopen'),
    }).subscribe(ok => {
      if (!ok) return;
      this.busyId.set(a.id);
      this.svc.setSettled(a.id, settled).subscribe({
        next:  () => { this.busyId.set(null); this.toast.success(this.translate.instant(settled ? 'cashAdvances.markedSettled' : 'cashAdvances.markedOutstanding')); this.load(); },
        error: (e) => { this.busyId.set(null); this.toast.error(apiErrorMessage(e) ?? this.translate.instant('cashAdvances.saveFailed')); },
      });
    });
  }

  remove(a: CashAdvanceDto): void {
    this.confirmSvc.confirm({
      title:        this.translate.instant('cashAdvances.confirmDeleteTitle'),
      message:      this.translate.instant('cashAdvances.confirmDeleteMessage'),
      confirmLabel: this.translate.instant('common.delete'),
      danger:       true,
    }).subscribe(ok => {
      if (!ok) return;
      this.busyId.set(a.id);
      this.svc.delete(a.id).subscribe({
        next:  () => { this.busyId.set(null); this.toast.success(this.translate.instant('cashAdvances.deleted')); this.load(); },
        error: (e) => { this.busyId.set(null); this.toast.error(apiErrorMessage(e) ?? this.translate.instant('cashAdvances.deleteFailed')); },
      });
    });
  }

  /** Local yyyy-MM-dd (no UTC shift) for the DateOnly the API expects. */
  private toYmd(d: Date): string {
    const y = d.getFullYear(), m = String(d.getMonth() + 1).padStart(2, '0'), day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }
}
