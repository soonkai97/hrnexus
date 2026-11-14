import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import {
  CanComponentDeactivate,
  CarryOverMode,
  CompanyPolicyService,
  ToastService,
  apiErrorMessage,
  leaveHours,
} from 'shared-lib';

interface PolicyForm {
  /** ISO weekday numbers, 1 = Monday ... 7 = Sunday. */
  workingDays: number[];
  workdayHours: number | null;
  annualLeaveAdvanceDays: number | null;
  maxLeaveDays: number | null;
  carryOverMode: CarryOverMode;
  carryOverMaxHours: number | null;
  unpaidLeaveDivisor: number | null;
}

const inRange = (v: number | null, min: number, max: number): v is number =>
  v !== null && Number.isFinite(v) && v >= min && v <= max;

/**
 * Company policy: the working week and leave rules that differ from one company to the next.
 * Everyone's leave previews read it; the server enforces it. Changes are not retroactive
 * (recorded leave keeps its hours).
 */
@Component({
  selector: 'app-policy',
  standalone: true,
  imports: [FormsModule, Button, Card, InputText, Message, Select, Skeleton],
  templateUrl: './policy.component.html',
  styleUrl: './policy.component.scss',
})
export class PolicyComponent implements OnInit, CanComponentDeactivate {
  private readonly policySvc = inject(CompanyPolicyService);
  private readonly toast = inject(ToastService);

  readonly weekdays = [
    { iso: 1, label: 'Mon' },
    { iso: 2, label: 'Tue' },
    { iso: 3, label: 'Wed' },
    { iso: 4, label: 'Thu' },
    { iso: 5, label: 'Fri' },
    { iso: 6, label: 'Sat' },
    { iso: 7, label: 'Sun' },
  ];

  readonly carryOverOptions: { label: string; value: CarryOverMode }[] = [
    { label: 'Unused leave lapses', value: 'none' },
    { label: 'Up to a cap carries forward', value: 'capped' },
    { label: 'All unused leave carries forward (cumulative)', value: 'unlimited' },
  ];

  loading = signal(true);
  saving = signal(false);
  error = signal('');
  /** True when the server refused the save because someone else changed the policy first. */
  conflict = signal(false);

  form: PolicyForm = this.formFromPolicy();
  /** The form as last loaded or saved, to tell whether there is anything unsaved. */
  private savedJson = JSON.stringify(this.form);

  ngOnInit(): void {
    this.policySvc.load().subscribe(() => {
      this.fill();
      this.loading.set(false);
    });
  }

  // ── Working days ─────────────────────────────────────────────────────────────

  isDayOn(iso: number): boolean {
    return this.form.workingDays.includes(iso);
  }

  toggleDay(iso: number): void {
    const days = this.isDayOn(iso)
      ? this.form.workingDays.filter((d) => d !== iso)
      : [...this.form.workingDays, iso];
    this.form.workingDays = days.sort((a, b) => a - b);
  }

  showCarryOverCap(): boolean {
    return this.form.carryOverMode === 'capped';
  }

  // ── Validation (mirrors the server, which refuses the same values) ───────────

  validationError(): string {
    const f = this.form;
    if (f.workingDays.length === 0) return 'Choose at least one working day.';
    if (!(
      f.workdayHours !== null &&
      Number.isFinite(f.workdayHours) &&
      f.workdayHours > 0 &&
      f.workdayHours <= 24
    ))
      return 'Hours per working day must be more than 0 and at most 24.';
    if (!inRange(f.annualLeaveAdvanceDays, 0, 60))
      return 'Annual leave notice must be between 0 and 60 days.';
    if (!inRange(f.maxLeaveDays, 1, 366))
      return 'The longest single leave request must be between 1 and 366 days.';
    if (this.showCarryOverCap() && !inRange(f.carryOverMaxHours, 0, 720))
      return 'The carry-over cap must be between 0 and 720 hours.';
    if (!inRange(f.unpaidLeaveDivisor, 20, 31))
      return 'The unpaid-leave divisor must be between 20 and 31.';
    return '';
  }

  // ── Live example: what the current form would charge ─────────────────────────

  examples(): string[] {
    const schedule = { days: this.form.workingDays, hours: this.form.workdayHours ?? 0 };
    // 2026-06-05 is a Friday, 2026-06-06 a Saturday, 2026-06-08 a Monday.
    const fridayToMonday = leaveHours('2026-06-05', '2026-06-08', null, null, schedule);
    const saturday = leaveHours('2026-06-06', '2026-06-06', null, null, schedule);
    return [
      fridayToMonday === null
        ? 'A request from Friday to Monday has no working day, so it is refused.'
        : `A request from Friday to Monday costs ${fridayToMonday} hours.`,
      saturday === null
        ? 'Saturday is not a working day, so a request for it alone is refused.'
        : `A request for a single Saturday costs ${saturday} hours.`,
    ];
  }

  // ── Saving ───────────────────────────────────────────────────────────────────

  hasUnsavedChanges(): boolean {
    return JSON.stringify(this.form) !== this.savedJson;
  }

  save(): void {
    const invalid = this.validationError();
    if (invalid) {
      this.error.set(invalid);
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.conflict.set(false);
    const f = this.form;
    this.policySvc
      .save({
        workingDays: f.workingDays,
        workdayHours: f.workdayHours!,
        annualLeaveAdvanceDays: f.annualLeaveAdvanceDays!,
        maxLeaveDays: f.maxLeaveDays!,
        carryOverMode: f.carryOverMode,
        carryOverMaxHours: f.carryOverMaxHours ?? 0,
        unpaidLeaveDivisor: f.unpaidLeaveDivisor!,
      })
      .subscribe({
        next: () => {
          this.savedJson = JSON.stringify(this.form);
          this.toast.success('Company policy saved.');
          this.saving.set(false);
        },
        error: (err) => {
          this.conflict.set(err?.status === 409);
          this.error.set(apiErrorMessage(err) ?? 'Could not save the company policy.');
          this.saving.set(false);
        },
      });
  }

  /** Throw away the edits and take whatever is saved now (after a conflict, or to start over). */
  reload(): void {
    this.error.set('');
    this.conflict.set(false);
    this.loading.set(true);
    this.policySvc.load().subscribe(() => {
      this.fill();
      this.loading.set(false);
    });
  }

  private fill(): void {
    this.form = this.formFromPolicy();
    this.savedJson = JSON.stringify(this.form);
  }

  private formFromPolicy(): PolicyForm {
    const p = this.policySvc.policy();
    return {
      workingDays: [...p.workingDays],
      workdayHours: p.workdayHours,
      annualLeaveAdvanceDays: p.annualLeaveAdvanceDays,
      maxLeaveDays: p.maxLeaveDays,
      carryOverMode: p.carryOverMode,
      carryOverMaxHours: p.carryOverMaxHours,
      unpaidLeaveDivisor: p.unpaidLeaveDivisor,
    };
  }
}
