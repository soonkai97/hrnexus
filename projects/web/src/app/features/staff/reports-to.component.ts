import { Component, Input, OnChanges, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { Staff, StaffStore } from 'shared-lib';

interface ManagerOption {
  label: string;
  value: string;
}

/**
 * Who this person reports to. It decides whose payroll line a manager may read and sign off, so only staff.manage can change it
 * (the server enforces that, refuses a loop, and refuses a change while the person's pay is in a run under review). Everyone
 * who can open the staff page sees it. The manager is chosen by searching the staff list on the server, so it works however
 * many people there are.
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
            [filter]="true"
            filterPlaceholder="Search by name or Staff ID"
            (onFilter)="search($any($event).filter)"
            appendTo="body"
            styleClass="rt-select"
            ariaLabel="Reports to"
          />
          <p-button
            label="Save"
            severity="primary"
            [disabled]="!changed || saving()"
            [loading]="saving()"
            (onClick)="save()"
          />
        </div>
        <small class="rt-hint"
          >Their reporting manager can read and sign off their pay each month.</small
        >
      }
    </p-card>
  `,
  styles: [
    `
      .rt-text {
        margin: 0.5rem 0 0;
      }
      .rt-row {
        display: flex;
        flex-wrap: wrap;
        gap: 0.6rem;
        align-items: center;
        margin-top: 0.6rem;
      }
      :host ::ng-deep .rt-select {
        min-width: min(320px, 100%);
      }
      .rt-hint {
        display: block;
        margin-top: 0.5rem;
        color: var(--nc-text-muted, #6b7280);
      }
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

  private readonly store = inject(StaffStore);

  managerId: string | null = null;
  readonly saving = signal(false);
  /** The current manager plus whoever the last search found (active people other than this one). */
  readonly options = signal<ManagerOption[]>([]);

  private readonly searches = new Subject<string>();

  constructor() {
    this.searches
      .pipe(
        debounceTime(250),
        switchMap((q) => this.store.findPeople(q)),
        takeUntilDestroyed(),
      )
      .subscribe((people) => this.setOptions(people));
  }

  ngOnChanges(): void {
    this.managerId = this.currentId;
    this.options.set(
      this.currentId
        ? [{ label: this.currentName ?? 'Unknown person', value: this.currentId }]
        : [],
    );
    if (this.canManage && this.staffId) this.search('');
  }

  search(q: string): void {
    this.searches.next((q ?? '').trim());
  }

  private setOptions(people: Staff[]): void {
    const found = people
      .filter((s) => s.isActive && s.id !== this.staffId)
      .map((s) => ({ label: `${s.staffId} — ${s.fullName}`, value: s.id }));
    const keep =
      this.currentId && !found.some((o) => o.value === this.currentId)
        ? [{ label: this.currentName ?? 'Unknown person', value: this.currentId }]
        : [];
    this.options.set([...keep, ...found]);
  }

  get changed(): boolean {
    return (this.managerId ?? null) !== (this.currentId ?? null);
  }

  save(): void {
    if (!this.changed || this.saving()) return;
    this.saving.set(true);
    this.store.setReportsTo(this.staffId, this.managerId ?? null).subscribe({
      next: () => {
        this.currentId = this.managerId ?? null;
        this.currentName = this.options().find((o) => o.value === this.managerId)?.label ?? null;
      },
      complete: () => this.saving.set(false),
    });
  }
}
