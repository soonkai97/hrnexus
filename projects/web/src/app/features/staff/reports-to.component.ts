import { Component, Input, OnChanges, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { StaffManagementService, StaffStore, ToastService, apiErrorMessage } from 'shared-lib';

/**
 * Who this person reports to. It decides whose payroll line a manager may read and sign off, so only staff.manage can change it
 * (the server enforces that, refuses a loop, and refuses a change while the person's pay is in a run being reviewed). Everyone
 * who can open the staff page sees it.
 */
@Component({
  selector: 'app-reports-to',
  standalone: true,
  imports: [FormsModule, Button, Card, Select],
  template: `
    <p-card styleClass="nc-card" class="mb-4">
      <h3 class="m-0">Reports to</h3>
      @if (!canManage) {
        <p class="rt-text">{{ currentName || 'Nobody' }}</p>
      } @else {
        <div class="rt-row">
          <p-select
            inputId="reports-to"
            [options]="options()"
            [(ngModel)]="managerId"
            optionLabel="label"
            optionValue="value"
            placeholder="Nobody"
            [showClear]="true"
            appendTo="body"
            styleClass="rt-select"
            ariaLabel="Reports to"
          />
          <p-button label="Save" severity="primary" [disabled]="!changed || saving()" [loading]="saving()" (onClick)="save()" />
        </div>
        <small class="rt-hint">Their reporting manager can read and sign off their pay each month.</small>
      }
    </p-card>
  `,
  styles: [
    `
      .rt-text { margin: 0.5rem 0 0; }
      .rt-row { display: flex; flex-wrap: wrap; gap: 0.6rem; align-items: center; margin-top: 0.6rem; }
      :host ::ng-deep .rt-select { min-width: min(320px, 100%); }
      .rt-hint { display: block; margin-top: 0.5rem; color: var(--nc-text-muted, #6b7280); }
    `,
  ],
})
export class ReportsToComponent implements OnChanges {
  /** The staff member's id (the Guid the API uses). */
  @Input({ required: true }) staffId = '';
  /** Who they report to now (null = nobody). */
  @Input() currentId: string | null = null;
  @Input() currentName: string | null = null;
  /** Whether the signed-in person holds staff.manage (the server enforces it; this only hides the editor). */
  @Input() canManage = false;

  private readonly staffSvc = inject(StaffManagementService);
  private readonly store = inject(StaffStore);
  private readonly toast = inject(ToastService);

  managerId: string | null = null;
  readonly saving = signal(false);

  /** Active people other than this one (the server also refuses an inactive manager and a loop). */
  readonly options = computed(() =>
    this.store
      .staff()
      .filter((s) => s.isActive && s.id !== this.staffId)
      .map((s) => ({ label: `${s.staffId} — ${s.fullName}`, value: s.id })),
  );

  ngOnChanges(): void {
    this.managerId = this.currentId;
  }

  get changed(): boolean {
    return (this.managerId ?? null) !== (this.currentId ?? null);
  }

  save(): void {
    if (!this.changed || this.saving()) return;
    this.saving.set(true);
    this.staffSvc.setReportsTo(this.staffId, this.managerId ?? null).subscribe({
      next: () => {
        this.saving.set(false);
        this.currentId = this.managerId ?? null;
        this.currentName = this.options().find((o) => o.value === this.managerId)?.label ?? null;
        this.toast.success('Saved.');
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to save.');
        this.saving.set(false);
      },
    });
  }
}
