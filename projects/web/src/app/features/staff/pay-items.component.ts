import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Checkbox } from 'primeng/checkbox';
import { DatePicker } from 'primeng/datepicker';
import { Dialog } from 'primeng/dialog';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { PrimeTemplate } from 'primeng/api';
import { Select } from 'primeng/select';
import { Tag } from 'primeng/tag';
import { ConfirmService, PayItem, PayrollService, ToastService, apiErrorMessage, rm, todayLocalString } from 'shared-lib';

/**
 * A person's recurring allowances and deductions (a transport allowance, a parking fee). Shown to anyone with payroll.view;
 * payroll.manage can add, end and delete. The server decides every change: an item a submitted or locked run has used can
 * be ended but not deleted, and cannot start in a month a run already covers. Money is only formatted here.
 */
@Component({
  selector: 'app-pay-items',
  standalone: true,
  imports: [DatePipe, FormsModule, Button, Card, Checkbox, DatePicker, Dialog, InputNumber, InputText, PrimeTemplate, Select, Tag],
  template: `
    <p-card styleClass="nc-card" class="mb-4">
      <div class="pi-head">
        <h3 class="m-0">Allowances and deductions</h3>
        @if (canManage) {
          <p-button icon="pi pi-plus" label="Add" severity="primary" (onClick)="openAdd()" />
        }
      </div>
      @if (items().length === 0) {
        <p class="pi-empty">No recurring allowances or deductions.</p>
      } @else {
        <ul class="pi-list">
          @for (i of items(); track i.id) {
            <li class="pi-row">
              <div class="pi-main">
                <strong>{{ i.reason }}</strong>
                <p-tag [value]="i.kind === 'allowance' ? 'Allowance' : 'Deduction'" [severity]="i.kind === 'allowance' ? 'success' : 'warn'" />
                @if (isEnded(i)) { <p-tag value="Ended" severity="secondary" /> }
              </div>
              <div class="pi-amount">{{ rm(i.amount) }} a month</div>
              <div class="pi-meta">
                From {{ i.from | date: 'dd/MM/yyyy' }}@if (i.to) { to {{ i.to | date: 'dd/MM/yyyy' }} }
                @if (i.kind === 'allowance') { · counts for {{ countsText(i) }} }
                @else { · priority {{ i.priority }} }
              </div>
              @if (canManage) {
                <div class="pi-actions">
                  <p-button label="End" icon="pi pi-calendar-times" [text]="true" size="small" severity="secondary" (onClick)="openEnd(i)" />
                  @if (!i.inUse) {
                    <p-button label="Delete" icon="pi pi-trash" [text]="true" size="small" severity="danger" (onClick)="remove(i)" />
                  }
                </div>
              }
            </li>
          }
        </ul>
      }
    </p-card>

    <p-dialog [visible]="showAdd()" (visibleChange)="!$event && showAdd.set(false)" header="Add allowance or deduction"
              [modal]="true" [style]="{ width: 'min(460px, 94vw)' }" [draggable]="false" appendTo="body">
      <div class="pi-field">
        <label for="pi-kind">Kind</label>
        <p-select inputId="pi-kind" [options]="kinds" [(ngModel)]="form.kind" optionLabel="label" optionValue="value" appendTo="body" styleClass="w-full" />
      </div>
      <div class="pi-field">
        <label for="pi-reason"><span class="req">*</span> Reason</label>
        <input id="pi-reason" pInputText type="text" [(ngModel)]="form.reason" maxlength="200" placeholder="e.g. Transport" />
      </div>
      <div class="pi-field">
        <label for="pi-amount"><span class="req">*</span> Amount each month</label>
        <p-inputnumber inputId="pi-amount" [(ngModel)]="form.amount" mode="currency" currency="MYR" locale="en-MY" [min]="0" />
      </div>
      @if (form.kind === 'allowance') {
        <div class="pi-field"><p-checkbox inputId="pi-epf" [(ngModel)]="form.countsForEpf" [binary]="true" label="Counts towards EPF" /></div>
        <div class="pi-field"><p-checkbox inputId="pi-socso" [(ngModel)]="form.countsForSocsoEis" [binary]="true" label="Counts towards SOCSO and EIS" /></div>
      } @else {
        <div class="pi-field">
          <label for="pi-priority">Priority (lowest is taken first when pay cannot cover everything)</label>
          <p-inputnumber inputId="pi-priority" [(ngModel)]="form.priority" [min]="0" [max]="1000" [useGrouping]="false" />
        </div>
      }
      <div class="pi-field">
        <label for="pi-from"><span class="req">*</span> From</label>
        <p-datepicker inputId="pi-from" [(ngModel)]="form.from" dateFormat="yy-mm-dd" dataType="string" appendTo="body" />
      </div>
      <div class="pi-field">
        <label for="pi-to">Until (optional)</label>
        <p-datepicker inputId="pi-to" [(ngModel)]="form.to" dateFormat="yy-mm-dd" dataType="string" appendTo="body" [showClear]="true" />
      </div>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" [text]="true" severity="secondary" (onClick)="showAdd.set(false)" />
        <p-button label="Add" severity="primary" [disabled]="!isFormValid || saving()" [loading]="saving()" (onClick)="add()" />
      </ng-template>
    </p-dialog>

    <p-dialog [visible]="endTarget() !== null" (visibleChange)="!$event && endTarget.set(null)" header="End this item"
              [modal]="true" [style]="{ width: 'min(420px, 94vw)' }" [draggable]="false" appendTo="body">
      <p class="pi-intro">The last day it applies. It is not paid after that.</p>
      <div class="pi-field">
        <label for="pi-end"><span class="req">*</span> Last day</label>
        <p-datepicker inputId="pi-end" [(ngModel)]="endDate" dateFormat="yy-mm-dd" dataType="string" appendTo="body" />
      </div>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" [text]="true" severity="secondary" (onClick)="endTarget.set(null)" />
        <p-button label="End" severity="primary" [disabled]="!endDate || saving()" [loading]="saving()" (onClick)="end()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .pi-head { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 0.5rem; }
      .pi-empty { margin: 0.5rem 0 0; color: var(--nc-text-muted, #6b7280); font-size: 0.9rem; }
      .pi-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; }
      .pi-row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem 1rem; padding: 0.6rem 0; border-top: 1px solid var(--nc-border-subtle, #e5e7eb); }
      .pi-main { display: flex; align-items: center; gap: 0.5rem; flex: 1 1 14rem; flex-wrap: wrap; }
      .pi-amount { font-weight: 600; }
      .pi-meta { flex: 1 1 100%; color: var(--nc-text-muted, #6b7280); font-size: 0.82rem; }
      .pi-actions { margin-left: auto; display: flex; gap: 0.25rem; }
      .pi-field { display: flex; flex-direction: column; gap: 0.35rem; margin-bottom: 0.9rem; }
      .pi-field label { font-size: 0.8rem; font-weight: 600; }
      .pi-intro { margin: 0 0 0.9rem; color: var(--nc-text-muted, #6b7280); font-size: 0.85rem; }
      .req { color: #dc2626; }
    `,
  ],
})
export class PayItemsComponent implements OnChanges {
  /** The staff member's id (the Guid the API uses). */
  @Input({ required: true }) staffId = '';
  /** Whether the signed-in person holds payroll.manage (the server enforces it; this only hides the buttons). */
  @Input() canManage = false;

  private readonly svc = inject(PayrollService);
  private readonly toast = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);

  readonly rm = rm;
  readonly items = signal<PayItem[]>([]);
  readonly saving = signal(false);
  readonly showAdd = signal(false);
  readonly endTarget = signal<PayItem | null>(null);
  endDate = '';

  readonly kinds = [
    { label: 'Allowance (adds to pay)', value: 'allowance' as const },
    { label: 'Deduction (takes from pay)', value: 'deduction' as const },
  ];
  form = this.blank();

  private blank() {
    return {
      kind: 'allowance' as 'allowance' | 'deduction',
      reason: '',
      amount: null as number | null,
      countsForEpf: true,
      countsForSocsoEis: true,
      priority: 0,
      from: '',
      to: '' as string | null,
    };
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['staffId'] && this.staffId) this.load();
  }

  load(): void {
    this.svc.getPayItems(this.staffId).subscribe({
      next: (rows) => this.items.set(rows),
      error: (e: unknown) => this.toast.error(apiErrorMessage(e) ?? 'Failed to load allowances and deductions.'),
    });
  }

  isEnded(i: PayItem): boolean {
    return i.to !== null && i.to < todayLocalString();
  }

  countsText(i: PayItem): string {
    const parts = [i.countsForEpf ? 'EPF' : '', i.countsForSocsoEis ? 'SOCSO and EIS' : ''].filter(Boolean);
    return parts.length ? parts.join(' and ') : 'neither EPF nor SOCSO';
  }

  get isFormValid(): boolean {
    return this.form.reason.trim().length > 0 && (this.form.amount ?? 0) > 0 && !!this.form.from;
  }

  openAdd(): void {
    this.form = this.blank();
    this.form.from = todayLocalString();
    this.showAdd.set(true);
  }

  add(): void {
    if (!this.isFormValid || this.saving()) return;
    const f = this.form;
    this.saving.set(true);
    this.svc
      .addPayItem(this.staffId, {
        kind: f.kind,
        reason: f.reason.trim(),
        amount: f.amount!,
        countsForEpf: f.countsForEpf,
        countsForSocsoEis: f.countsForSocsoEis,
        priority: f.kind === 'deduction' ? (f.priority ?? 0) : 0,
        from: f.from,
        to: f.to ? f.to : null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.showAdd.set(false);
          this.toast.success('Added.');
          this.load();
        },
        error: (e: unknown) => {
          this.toast.error(apiErrorMessage(e) ?? 'Failed to add it.');
          this.saving.set(false);
        },
      });
  }

  openEnd(i: PayItem): void {
    this.endDate = i.to ?? todayLocalString();
    this.endTarget.set(i);
  }

  end(): void {
    const i = this.endTarget();
    if (!i || !this.endDate || this.saving()) return;
    this.saving.set(true);
    this.svc.endPayItem(this.staffId, i.id, this.endDate).subscribe({
      next: () => {
        this.saving.set(false);
        this.endTarget.set(null);
        this.toast.success('End date saved.');
        this.load();
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to end it.');
        this.saving.set(false);
      },
    });
  }

  remove(i: PayItem): void {
    this.confirmSvc.confirm({ message: `Delete "${i.reason}"? Use this only for an item entered by mistake.`, danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.svc.deletePayItem(this.staffId, i.id).subscribe({
        next: () => {
          this.toast.success('Deleted.');
          this.load();
        },
        error: (e: unknown) => this.toast.error(apiErrorMessage(e) ?? 'Failed to delete it.'),
      });
    });
  }
}
