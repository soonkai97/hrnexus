import { Component, Input, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Checkbox } from 'primeng/checkbox';
import { Dialog } from 'primeng/dialog';
import { InputNumber } from 'primeng/inputnumber';
import { Message } from 'primeng/message';
import { PrimeTemplate } from 'primeng/api';
import { Skeleton } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Textarea } from 'primeng/textarea';
import {
  ConfirmService,
  PayrollItemLine,
  PayrollRunAction,
  PayrollRunLine,
  PayrollRunStore,
  ViewModeService,
  ViewToggleComponent,
  describeStep,
  initials,
  monthLabel,
  rm,
  runStateTag,
  sectionLabel,
} from 'shared-lib';

interface ItemGroup {
  section: string;
  label: string;
  items: PayrollItemLine[];
}

const SECTION_ORDER = [
  'Earning',
  'GrossDeduction',
  'EmployeeStatutory',
  'Tax',
  'OtherDeduction',
  'EmployerContribution',
];

/**
 * One month's payroll run in full: its state and what to do next, every person's figures, and the steps taken so far. The page
 * never calls the API: it reads and acts through `PayrollRunStore`. The server says which buttons the signed-in person may press
 * (`allowedActions`, and `canSignOff` on each line); the page draws only those and never works out who may do what. Money is only
 * formatted here, never calculated.
 */
@Component({
  selector: 'app-payroll-run-detail',
  standalone: true,
  imports: [
    DatePipe,
    RouterLink,
    FormsModule,
    Button,
    Card,
    Checkbox,
    Dialog,
    InputNumber,
    Message,
    PrimeTemplate,
    Skeleton,
    TableModule,
    Tag,
    Textarea,
    ViewToggleComponent,
  ],
  templateUrl: './payroll-run-detail.component.html',
  styleUrl: './payroll-run-detail.component.scss',
})
export class PayrollRunDetailComponent implements OnInit, OnDestroy {
  /** The run's id, bound from the route. */
  @Input() id = '';

  private readonly store = inject(PayrollRunStore);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly router = inject(Router);
  private readonly views = inject(ViewModeService);

  readonly viewMode = this.views.mode('payroll-run-lines');
  readonly monthLabel = monthLabel;
  readonly rm = rm;
  readonly tag = runStateTag;
  readonly sectionLabel = sectionLabel;
  readonly describeStep = describeStep;
  readonly initials = initials;

  readonly detail = this.store.detail;
  readonly loading = this.store.detailLoading;
  readonly busy = this.store.busy;
  readonly summary = this.store.summary;
  readonly lines = this.store.lines;
  readonly included = computed(() => this.lines().filter((l) => !l.excluded));
  readonly waiting = computed(() => this.included().filter((l) => !l.signedOffBy));
  readonly allSigned = computed(() => this.included().length > 0 && this.waiting().length === 0);
  readonly unverified = computed(() => this.detail()?.unverifiedRates ?? []);

  /** Whether the server offers the signed-in person this action on this run. */
  can(action: PayrollRunAction): boolean {
    return this.store.can(action);
  }

  ngOnInit(): void {
    this.store.open(this.id);
  }

  ngOnDestroy(): void {
    this.store.close();
  }

  // ── Display helpers ──────────────────────────────────────────────────────────

  banner(): { severity: 'info' | 'warn' | 'success'; text: string } {
    const s = this.summary();
    if (!s) return { severity: 'info', text: '' };
    switch (s.state) {
      case 'draft':
        return {
          severity: 'info',
          text: 'Draft. Check each person, add their PCB where it applies, then submit the run for sign-off.',
        };
      case 'submitted':
        return {
          severity: 'info',
          text: `${s.signedOff} of ${s.people} signed off. The head of HR approves once every line is signed.`,
        };
      case 'approved':
        return { severity: 'warn', text: 'Approved. Lock it once the month has been paid.' };
      case 'locked':
        return { severity: 'success', text: 'Locked. This month is paid out.' };
    }
  }

  /**
   * A warning that stops the run being submitted. An unverified rate is blocking in the calculator but does not stop a submit
   * (the accountant verifies it before approval, and the page says so above), so it is shown as a note, not as "to fix".
   */
  mustFix(w: { code: string; blocking: boolean }): boolean {
    return w.blocking && w.code !== 'unverified-rate';
  }

  blockingCount(l: PayrollRunLine): number {
    return l.warnings.filter((w) => this.mustFix(w)).length;
  }

  noteCount(l: PayrollRunLine): number {
    return l.warnings.filter((w) => !this.mustFix(w)).length;
  }

  groups(l: PayrollRunLine): ItemGroup[] {
    return SECTION_ORDER.map((section) => ({
      section,
      label: sectionLabel(section),
      items: l.items.filter((i) => i.section === section),
    })).filter((g) => g.items.length > 0);
  }

  // ── Draft: recalculate, submit, delete ───────────────────────────────────────

  recalculate(): void {
    this.store.recalculate().subscribe();
  }

  submit(): void {
    this.store.submit().subscribe();
  }

  deleteRun(): void {
    this.confirmSvc
      .confirm({
        title: 'Delete payroll run',
        message: 'Delete this draft run? Nothing has been approved or deducted yet.',
        confirmLabel: 'Delete',
        danger: true,
      })
      .subscribe((ok) => {
        if (ok) this.store.remove().subscribe(() => void this.router.navigate(['/payroll-runs']));
      });
  }

  // ── Review: sign off, approve, return ────────────────────────────────────────

  signOff(l: PayrollRunLine): void {
    this.store.signOff(l).subscribe(() => this.closeLine());
  }

  signOffAll(): void {
    this.store.signOffAll().subscribe();
  }

  approve(): void {
    this.confirmSvc
      .confirm({
        message: `Approve ${monthLabel(this.summary()!.year, this.summary()!.month)}? Every line is signed off.`,
      })
      .subscribe((ok) => {
        if (ok) this.store.approve().subscribe();
      });
  }

  readonly showReturn = signal(false);
  returnReason = '';

  openReturn(): void {
    this.returnReason = '';
    this.showReturn.set(true);
  }

  confirmReturn(): void {
    if (!this.returnReason.trim()) return;
    this.store.returnRun(this.returnReason.trim()).subscribe(() => this.showReturn.set(false));
  }

  // ── Lock and reopen ──────────────────────────────────────────────────────────

  lock(): void {
    this.confirmSvc
      .confirm({
        message:
          'Lock this month? It marks the month as paid and records the cash advance repayments that were taken.',
      })
      .subscribe((ok) => {
        if (ok) this.store.lock().subscribe();
      });
  }

  reopen(): void {
    this.confirmSvc
      .confirm({
        message:
          "Reopen this month for correction? The lock and this run's cash advance repayments are undone.",
        danger: true,
      })
      .subscribe((ok) => {
        if (ok) this.store.reopen().subscribe();
      });
  }

  // ── One person ───────────────────────────────────────────────────────────────

  /** The open person. It follows the store, so after a refusal the dialog shows the line as it is now. */
  private readonly selectedId = signal<string | null>(null);
  readonly selected = computed(
    () => this.lines().find((l) => l.employeeId === this.selectedId()) ?? null,
  );
  editPcb: number | null = null;
  editExcluded = false;
  editReason = '';

  openLine(l: PayrollRunLine): void {
    this.editPcb = l.pcb;
    this.editExcluded = l.excluded;
    this.editReason = l.excludedReason ?? '';
    this.selectedId.set(l.employeeId);
  }

  closeLine(): void {
    this.selectedId.set(null);
  }

  get isLineValid(): boolean {
    return (
      (this.editPcb === null || this.editPcb >= 0) &&
      (!this.editExcluded || this.editReason.trim().length > 0)
    );
  }

  saveLine(): void {
    const l = this.selected();
    if (!l || !this.isLineValid) return;
    this.store
      .updateLine(l.employeeId, {
        pcb: this.editPcb,
        excluded: this.editExcluded,
        excludedReason: this.editExcluded ? this.editReason.trim() : null,
      })
      .subscribe(() => this.closeLine());
  }
}
