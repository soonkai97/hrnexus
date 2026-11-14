import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, formatDate } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Textarea } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';
import {
  PayrollRatesService,
  PermissionService,
  StatutoryRate,
  RateScheme,
  RateRounding,
  ToastService,
  ConfirmService,
  apiErrorMessage,
} from 'shared-lib';

const SCHEME_LABELS: Record<RateScheme, string> = {
  epf: 'EPF',
  socso: 'SOCSO (category 1)',
  eis: 'EIS',
  'hrd-compulsory': 'HRD Corp levy 1%',
  'hrd-optional': 'HRD Corp levy 0.5% (optional)',
  'minimum-wage': 'Minimum wage',
};

const CLASS_LABELS: Record<string, string> = {
  citizen: 'Citizen',
  'permanent-resident': 'Permanent resident',
};

const ROUNDING_TEXT: Record<RateRounding, string> = {
  none: '',
  'nearest-cent': 'rounded to the nearest sen',
  'nearest-5-sen': 'rounded to the nearest 5 sen',
  'up-to-next-ringgit': 'rounded up to the next ringgit',
};

/**
 * Read-mostly page for the statutory rate table payroll calculates with. Everyone with
 * payroll.view sees it; people holding payroll.rates.verify can also verify a row and switch the
 * optional HRD Corp levy. The server enforces both; the buttons are hidden as a convenience.
 * Money is only formatted for display here, never calculated.
 */
@Component({
  selector: 'app-payroll-rates',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    Button,
    Card,
    Dialog,
    Message,
    Skeleton,
    TableModule,
    Tag,
    Textarea,
    TooltipModule,
  ],
  templateUrl: './payroll-rates.component.html',
  styleUrl: './payroll-rates.component.scss',
})
export class PayrollRatesComponent implements OnInit {
  private readonly svc = inject(PayrollRatesService);
  private readonly perms = inject(PermissionService);
  private readonly toast = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);

  readonly rates = signal<StatutoryRate[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly anyUnverified = computed(() => this.rates().some((r) => !r.verified));

  /** Row ids whose band table is open. */
  readonly expanded = signal<ReadonlySet<string>>(new Set<string>());

  /** The row the verify dialog is open for (null = closed). */
  readonly verifyTarget = signal<StatutoryRate | null>(null);
  verifyNote = '';

  get canVerify(): boolean {
    return this.perms.has('payroll.rates.verify');
  }

  get isVerifyValid(): boolean {
    return this.verifyNote.trim().length > 0;
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (rows) => {
        this.rates.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.toast.error('Failed to load statutory rates.');
        this.loading.set(false);
      },
    });
  }

  // ── Display helpers (formatting only, no arithmetic on money) ────────────────

  schemeLabel(r: StatutoryRate): string {
    return SCHEME_LABELS[r.scheme] ?? r.scheme;
  }

  classLabel(r: StatutoryRate): string {
    return CLASS_LABELS[r.citizenshipClass] ?? r.citizenshipClass;
  }

  isHrd(r: StatutoryRate): boolean {
    return r.scheme === 'hrd-compulsory' || r.scheme === 'hrd-optional';
  }

  /** Band and table amounts: always two decimals. */
  money(n: number): string {
    return n.toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  ratesSummary(r: StatutoryRate): string {
    if (r.scheme === 'minimum-wage') {
      return r.amount === null ? '—' : `${this.rm(r.amount)} a month`;
    }
    if (r.kind === 'band-table') {
      return `${r.bands.length} wage ${r.bands.length === 1 ? 'band' : 'bands'}`;
    }
    const parts: string[] = [];
    if (r.employeeRate > 0) parts.push(`Employee ${this.pct(r.employeeRate)}%`);
    let employer = `Employer ${this.pct(r.employerRate)}%`;
    if (
      r.employerRateMode === 'whole-wage-by-threshold' &&
      r.employerRateThreshold !== null &&
      r.employerRateAbove !== null
    ) {
      employer += ` (${this.pct(r.employerRateAbove)}% above ${this.rm(r.employerRateThreshold)})`;
    }
    parts.push(employer);
    if (r.wageCeiling !== null) parts.push(`wages up to ${this.rm(r.wageCeiling)}`);
    const rounding = ROUNDING_TEXT[r.rounding];
    if (rounding) parts.push(rounding);
    return parts.join(' · ');
  }

  verifiedTitle(r: StatutoryRate): string {
    const when = r.verifiedAt ? formatDate(r.verifiedAt, 'dd/MM/yyyy', 'en-US', '+0800') : '';
    return `by ${r.verifiedBy ?? 'unknown'} on ${when}: ${r.verificationNote ?? ''}`;
  }

  isExpanded(id: string): boolean {
    return this.expanded().has(id);
  }

  toggleBands(id: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  // ── Verify ───────────────────────────────────────────────────────────────────

  openVerify(r: StatutoryRate): void {
    this.verifyNote = '';
    this.verifyTarget.set(r);
  }

  cancelVerify(): void {
    this.verifyTarget.set(null);
  }

  confirmVerify(): void {
    const target = this.verifyTarget();
    if (!target || !this.isVerifyValid || this.saving()) return;
    this.saving.set(true);
    this.svc.verify(target.id, this.verifyNote.trim()).subscribe({
      next: () => {
        this.toast.success('Rate marked as verified.');
        this.saving.set(false);
        this.verifyTarget.set(null);
        this.load();
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to verify the rate.');
        this.saving.set(false);
      },
    });
  }

  // ── Optional HRD levy switch ─────────────────────────────────────────────────

  toggleActive(r: StatutoryRate): void {
    const next = !r.active;
    this.confirmSvc
      .confirm({
        message: `Switch ${this.schemeLabel(r)} ${next ? 'on' : 'off'} for ${this.classLabel(r)}? It changes every payroll calculated from now on.`,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.svc.setActive(r.id, next).subscribe({
          next: () => {
            this.toast.success(next ? 'Levy switched on.' : 'Levy switched off.');
            this.load();
          },
          error: (e: unknown) =>
            this.toast.error(apiErrorMessage(e) ?? 'Failed to switch the levy.'),
        });
      });
  }

  /** A fraction as a percentage, up to 2 decimals (0.0175 -> "1.75"). Display only. */
  private pct(rate: number): string {
    return (rate * 100).toLocaleString('en-MY', { maximumFractionDigits: 2 });
  }

  private rm(n: number): string {
    return `RM${n.toLocaleString('en-MY', { maximumFractionDigits: 2 })}`;
  }
}
