import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
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
  AuthService,
  ConfirmService,
  PayrollItemLine,
  PayrollRunDetail,
  PayrollRunLine,
  PayrollRunService,
  PermissionService,
  ToastService,
  ViewModeService,
  ViewToggleComponent,
  apiErrorMessage,
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

const SECTION_ORDER = ['Earning', 'GrossDeduction', 'EmployeeStatutory', 'Tax', 'OtherDeduction', 'EmployerContribution'];

/**
 * One month's payroll run in full: its state and what to do next, every person's figures, and the steps taken so far. Each
 * button is shown only to someone who may press it and only in the state where it applies; the server decides every one of
 * them (permission, state, who prepared the run, whose pay it is) and says why when it refuses. Every action sends the
 * version the page read, so a page that is out of date is refused and reloaded instead of overwriting someone's change.
 * Money is only formatted here, never calculated.
 */
@Component({
  selector: 'app-payroll-run-detail',
  standalone: true,
  imports: [DatePipe, RouterLink, FormsModule, Button, Card, Checkbox, Dialog, InputNumber, Message, PrimeTemplate, Skeleton, TableModule, Tag, Textarea, ViewToggleComponent],
  templateUrl: './payroll-run-detail.component.html',
  styleUrl: './payroll-run-detail.component.scss',
})
export class PayrollRunDetailComponent implements OnInit {
  /** The run's id, bound from the route. */
  @Input() id = '';

  private readonly svc = inject(PayrollRunService);
  private readonly perms = inject(PermissionService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
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

  readonly detail = signal<PayrollRunDetail | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);

  readonly summary = computed(() => this.detail()?.summary ?? null);
  readonly state = computed(() => this.summary()?.state ?? 'draft');
  readonly lines = computed(() => this.detail()?.lines ?? []);
  readonly included = computed(() => this.lines().filter((l) => !l.excluded));
  readonly waiting = computed(() => this.included().filter((l) => !l.signedOffBy));
  readonly allSigned = computed(() => this.included().length > 0 && this.waiting().length === 0);
  readonly unverified = computed(() => this.detail()?.unverifiedRates ?? []);
  /** A run that was returned or reopened has history that must stay, so it cannot be deleted. */
  readonly canDeleteRun = computed(
    () => !(this.detail()?.steps ?? []).some((s) => s.action === 'returned' || s.action === 'reopened'),
  );

  private get me(): string | undefined {
    return this.auth.user()?.id;
  }
  get canManage(): boolean { return this.perms.has('payroll.manage'); }
  get canApprove(): boolean { return this.perms.has('payroll.approve'); }
  get canSignoff(): boolean { return this.perms.has('payroll.signoff'); }
  get canLock(): boolean { return this.perms.has('payroll.lock'); }
  get canReopen(): boolean { return this.perms.has('payroll.reopen'); }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(this.detail() === null);
    this.svc.get(this.id, true).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load the payroll run.');
        this.loading.set(false);
      },
    });
  }

  /** Runs one action, then reloads. On a refusal (a stale page, a rule) it says why and reloads so the page is current again. */
  private act(call: Observable<unknown>, success: string, after?: () => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    call.subscribe({
      next: () => {
        this.toast.success(success);
        this.busy.set(false);
        after?.();
        this.load();
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'That did not work.');
        this.busy.set(false);
        this.load();
      },
    });
  }

  private get version(): number {
    return this.summary()?.version ?? 0;
  }

  // ── Display helpers ──────────────────────────────────────────────────────────

  banner(): { severity: 'info' | 'warn' | 'success'; text: string } {
    const s = this.summary();
    if (!s) return { severity: 'info', text: '' };
    switch (s.state) {
      case 'draft':
        return { severity: 'info', text: 'Draft. Check each person, add their PCB where it applies, then submit the run for sign-off.' };
      case 'submitted':
        return { severity: 'info', text: `${s.signedOff} of ${s.people} signed off. The head of HR approves once every line is signed.` };
      case 'approved':
        return { severity: 'warn', text: 'Approved. Lock it once the month has been paid.' };
      case 'locked':
        return { severity: 'success', text: 'Locked. This month is paid out.' };
    }
  }

  blockingCount(l: PayrollRunLine): number {
    return l.warnings.filter((w) => w.blocking).length;
  }

  noteCount(l: PayrollRunLine): number {
    return l.warnings.filter((w) => !w.blocking).length;
  }

  groups(l: PayrollRunLine): ItemGroup[] {
    return SECTION_ORDER.map((section) => ({
      section,
      label: sectionLabel(section),
      items: l.items.filter((i) => i.section === section),
    })).filter((g) => g.items.length > 0);
  }

  canSignLine(l: PayrollRunLine): boolean {
    return this.state() === 'submitted' && !l.excluded && !l.signedOffBy && (this.canApprove || this.canSignoff) && l.employeeId !== this.me;
  }

  // ── Draft: recalculate, submit, delete ───────────────────────────────────────

  recalculate(): void {
    this.act(this.svc.recalculate(this.id, this.version), 'Everyone has been calculated again.');
  }

  submit(): void {
    this.act(this.svc.submit(this.id, this.version), 'Submitted for sign-off.');
  }

  deleteRun(): void {
    this.confirmSvc.confirm({ message: 'Delete this draft run? Nothing has been approved or deducted yet.', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.act(this.svc.delete(this.id), 'Draft run deleted.', () => void this.router.navigate(['/payroll-runs']));
    });
  }

  // ── Review: sign off, approve, return ────────────────────────────────────────

  signOff(l: PayrollRunLine): void {
    this.act(this.svc.signOff(this.id, l.employeeId, this.version), `Signed off ${l.name}.`, () => this.selected.set(null));
  }

  signOffAll(): void {
    this.act(this.svc.signOffAll(this.id, this.version), 'Every waiting line has been signed off.');
  }

  approve(): void {
    this.confirmSvc.confirm({ message: `Approve ${monthLabel(this.summary()!.year, this.summary()!.month)}? Every line is signed off.` }).subscribe((ok) => {
      if (ok) this.act(this.svc.approve(this.id, this.version), 'Approved.');
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
    this.act(this.svc.returnRun(this.id, this.returnReason.trim(), this.version), 'Returned for changes.', () => this.showReturn.set(false));
  }

  // ── Lock and reopen ──────────────────────────────────────────────────────────

  lock(): void {
    this.confirmSvc.confirm({ message: 'Lock this month? It marks the month as paid and records the cash advance repayments that were taken.' }).subscribe((ok) => {
      if (ok) this.act(this.svc.lock(this.id), 'Locked.');
    });
  }

  reopen(): void {
    this.confirmSvc.confirm({ message: 'Reopen this month for correction? The lock and this run\'s cash advance repayments are undone.', danger: true }).subscribe((ok) => {
      if (ok) this.act(this.svc.reopen(this.id), 'Reopened as a draft.');
    });
  }

  // ── One person ───────────────────────────────────────────────────────────────

  readonly selected = signal<PayrollRunLine | null>(null);
  editPcb: number | null = null;
  editExcluded = false;
  editReason = '';

  get canEditLine(): boolean {
    return this.state() === 'draft' && this.canManage;
  }

  openLine(l: PayrollRunLine): void {
    this.editPcb = l.pcb;
    this.editExcluded = l.excluded;
    this.editReason = l.excludedReason ?? '';
    this.selected.set(l);
  }

  get isLineValid(): boolean {
    return (this.editPcb === null || this.editPcb >= 0) && (!this.editExcluded || this.editReason.trim().length > 0);
  }

  saveLine(): void {
    const l = this.selected();
    if (!l || !this.isLineValid) return;
    this.act(
      this.svc.updateLine(this.id, l.employeeId, {
        pcb: this.editPcb,
        excluded: this.editExcluded,
        excludedReason: this.editExcluded ? this.editReason.trim() : null,
        expectedVersion: this.version,
      }),
      'Saved.',
      () => this.selected.set(null),
    );
  }
}
