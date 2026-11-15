import { Component, OnInit, computed, inject } from '@angular/core';
import { Button } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import {
  PayrollItemLine,
  PayrollRunLine,
  PayrollRunStore,
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
 * A manager's view of payroll: only the pay of the people who report to them, once a run has been submitted, with a button to
 * sign each person off. It needs only payroll.signoff, so a manager sees nothing of anyone else's pay. The server returns only
 * this manager's direct reports and refuses a sign-off for anyone else; it also says which lines may be signed (`canSignOff`).
 * The page reads and acts through `PayrollRunStore` and never calls the API.
 */
@Component({
  selector: 'app-my-team-pay',
  standalone: true,
  imports: [Button, Message, Skeleton, Tag],
  templateUrl: './my-team-pay.component.html',
  styleUrl: './my-team-pay.component.scss',
})
export class MyTeamPayComponent implements OnInit {
  private readonly store = inject(PayrollRunStore);

  readonly monthLabel = monthLabel;
  readonly rm = rm;
  readonly tag = runStateTag;
  readonly initials = initials;

  readonly runs = this.store.teamRuns;
  readonly runsLoading = this.store.teamRunsLoading;
  readonly selectedId = this.store.teamId;
  readonly team = this.store.team;
  readonly loading = this.store.teamLoading;
  readonly busy = this.store.busy;

  readonly lines = computed(() => this.team()?.lines ?? []);
  readonly waiting = computed(() => this.lines().filter((l) => !l.signedOffBy).length);

  ngOnInit(): void {
    this.store.loadTeamRuns();
  }

  select(runId: string): void {
    this.store.selectTeamRun(runId);
  }

  groups(l: PayrollRunLine): ItemGroup[] {
    return SECTION_ORDER.map((section) => ({
      section,
      label: sectionLabel(section),
      items: l.items.filter((i) => i.section === section),
    })).filter((g) => g.items.length > 0);
  }

  signOff(l: PayrollRunLine): void {
    this.store.signOffTeamLine(l).subscribe();
  }
}
