import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Button } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import {
  PayrollItemLine,
  PayrollRunLine,
  PayrollRunService,
  PayrollTeam,
  PayrollTeamRun,
  ToastService,
  apiErrorMessage,
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
 * A manager's view of payroll: only the pay of the people who report to them, once a run has been submitted, with a button to
 * sign each person off. It needs only payroll.signoff, so a manager sees nothing of anyone else's pay. The server returns only
 * this manager's direct reports and refuses a sign-off for anyone else.
 */
@Component({
  selector: 'app-my-team-pay',
  standalone: true,
  imports: [Button, Message, Skeleton, Tag],
  templateUrl: './my-team-pay.component.html',
  styleUrl: './my-team-pay.component.scss',
})
export class MyTeamPayComponent implements OnInit {
  private readonly svc = inject(PayrollRunService);
  private readonly toast = inject(ToastService);

  readonly monthLabel = monthLabel;
  readonly rm = rm;
  readonly tag = runStateTag;
  readonly initials = initials;

  readonly runs = signal<PayrollTeamRun[]>([]);
  readonly runsLoading = signal(true);
  readonly selectedId = signal<string | null>(null);
  readonly team = signal<PayrollTeam | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);

  readonly lines = computed(() => this.team()?.lines ?? []);
  readonly waiting = computed(() => this.lines().filter((l) => !l.signedOffBy).length);
  readonly canSign = computed(() => this.team()?.state === 'submitted');

  ngOnInit(): void {
    this.svc.teamRuns().subscribe({
      next: (rows) => {
        this.runs.set(rows);
        this.runsLoading.set(false);
        // Open what is waiting for this manager first, otherwise the newest run.
        const first = rows.find((r) => r.toSign > 0) ?? rows[0];
        if (first) this.select(first.runId);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load your team.');
        this.runsLoading.set(false);
      },
    });
  }

  select(runId: string): void {
    this.selectedId.set(runId);
    this.loadTeam();
  }

  private loadTeam(): void {
    const id = this.selectedId();
    if (!id) return;
    this.loading.set(this.team() === null);
    this.svc.team(id).subscribe({
      next: (t) => {
        this.team.set(t);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load your team.');
        this.loading.set(false);
      },
    });
  }

  groups(l: PayrollRunLine): ItemGroup[] {
    return SECTION_ORDER.map((section) => ({
      section,
      label: sectionLabel(section),
      items: l.items.filter((i) => i.section === section),
    })).filter((g) => g.items.length > 0);
  }

  signOff(l: PayrollRunLine): void {
    const t = this.team();
    if (!t || this.busy()) return;
    this.busy.set(true);
    this.svc.signOff(t.runId, l.employeeId, t.version).subscribe({
      next: () => {
        this.toast.success(`Signed off ${l.name}.`);
        this.busy.set(false);
        this.loadTeam();
      },
      error: (e: unknown) => {
        // A refusal (the run changed since this page was read, or the rules) says why; reload so the page is current again.
        this.toast.error(apiErrorMessage(e) ?? 'That did not work.');
        this.busy.set(false);
        this.loadTeam();
      },
    });
  }
}
