import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  PayrollRunLine,
  PayrollRunService,
  PayrollTeam,
  PayrollTeamRun,
  ToastService,
} from 'shared-lib';
import { MyTeamPayComponent } from './my-team-pay.component';

/**
 * A manager's view of their team's pay.
 *
 * WHY these tests matter: a manager holds only payroll.signoff. The page must open what is waiting for them first, must offer
 * the sign-off only while the run is open for it, and must show the server's reason (and reload) when a sign-off is refused
 * because the run changed since they looked.
 */
describe('MyTeamPayComponent', () => {
  let component: MyTeamPayComponent;
  let svc: {
    teamRuns: ReturnType<typeof vi.fn>;
    team: ReturnType<typeof vi.fn>;
    signOff: ReturnType<typeof vi.fn>;
  };
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const run = (over: Partial<PayrollTeamRun> = {}): PayrollTeamRun => ({
    runId: 'r1',
    year: 2026,
    month: 10,
    state: 'submitted',
    lines: 2,
    toSign: 2,
    ...over,
  });
  const line = (over: Partial<PayrollRunLine> = {}): PayrollRunLine => ({
    employeeId: 'e1',
    staffId: 'ALICE',
    name: 'Alice Tan',
    excluded: false,
    excludedReason: null,
    pcb: null,
    calculated: true,
    gross: 3000,
    net: 2500,
    employerCost: 3300,
    unappliedDeductions: 0,
    warnings: [],
    items: [],
    unpaidDays: [],
    signedOffBy: null,
    signedOffByName: null,
    signedOffAt: null,
    ...over,
  });
  const team = (over: Partial<PayrollTeam> = {}): PayrollTeam => ({
    runId: 'r1',
    year: 2026,
    month: 10,
    state: 'submitted',
    version: 9,
    lines: [line()],
    ...over,
  });

  beforeEach(async () => {
    svc = {
      teamRuns: vi.fn().mockReturnValue(of([run()])),
      team: vi.fn().mockReturnValue(of(team())),
      signOff: vi.fn().mockReturnValue(of({ version: 10 })),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [MyTeamPayComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PayrollRunService, useValue: svc },
        { provide: ToastService, useValue: toast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
    component = TestBed.createComponent(MyTeamPayComponent).componentInstance;
  });

  it('opens the run that is waiting for this manager first', () => {
    svc.teamRuns.mockReturnValue(
      of([run({ runId: 'new', toSign: 0 }), run({ runId: 'waiting', toSign: 3 })]),
    );

    component.ngOnInit();

    expect(component.selectedId()).toBe('waiting');
    expect(svc.team).toHaveBeenCalledWith('waiting');
  });

  it('opens the newest run when nothing is waiting', () => {
    svc.teamRuns.mockReturnValue(
      of([run({ runId: 'newest', toSign: 0 }), run({ runId: 'older', toSign: 0 })]),
    );

    component.ngOnInit();

    expect(component.selectedId()).toBe('newest');
  });

  it('shows an empty state when there are no runs for this manager', () => {
    svc.teamRuns.mockReturnValue(of([]));

    component.ngOnInit();

    expect(component.runs()).toEqual([]);
    expect(component.selectedId()).toBeNull();
    expect(svc.team).not.toHaveBeenCalled();
  });

  it('counts the lines that still need a signature and offers sign-off only while the run is submitted', () => {
    svc.team.mockReturnValue(
      of(
        team({
          lines: [line(), line({ employeeId: 'e2', signedOffBy: 'me', signedOffByName: 'Me' })],
        }),
      ),
    );
    component.ngOnInit();
    expect(component.waiting()).toBe(1);
    expect(component.canSign()).toBe(true);

    svc.team.mockReturnValue(of(team({ state: 'approved' })));
    component.select('r1');
    expect(component.canSign()).toBe(false);
  });

  it('switching runs loads that run', () => {
    component.ngOnInit();
    svc.team.mockClear();

    component.select('other');

    expect(component.selectedId()).toBe('other');
    expect(svc.team).toHaveBeenCalledWith('other');
  });

  /** WHY: sign-off acts on the run on screen; a slow answer for a tab the manager left must never replace the month they are looking at. */
  it('a slow answer for a run the manager already left is ignored', () => {
    const october = new Subject<PayrollTeam>();
    const september = new Subject<PayrollTeam>();
    svc.team.mockImplementation((id: string) => (id === 'oct' ? october : september));
    svc.teamRuns.mockReturnValue(of([run({ runId: 'oct' }), run({ runId: 'sep', month: 9 })]));
    component.ngOnInit(); // opens 'oct'

    component.select('sep');
    september.next(team({ runId: 'sep', month: 9 }));
    october.next(team({ runId: 'oct' })); // arrives last

    expect(component.selectedId()).toBe('sep');
    expect(component.team()?.runId).toBe('sep');
  });

  it('switching to another run clears the old one so it is never shown under the new tab', () => {
    component.ngOnInit();
    svc.team.mockReturnValue(new Subject<PayrollTeam>());

    component.select('other');

    expect(component.team()).toBeNull();
  });

  it('after a sign-off the tab badges are refreshed', () => {
    component.ngOnInit();
    svc.teamRuns.mockClear();
    svc.teamRuns.mockReturnValue(of([run({ toSign: 1 })]));

    component.signOff(component.lines()[0]!);

    expect(svc.teamRuns).toHaveBeenCalled();
    expect(component.runs()[0]!.toSign).toBe(1);
  });

  it('sign-off sends the person and the version the page read, then reloads', () => {
    component.ngOnInit();
    svc.team.mockClear();

    component.signOff(component.lines()[0]!);

    expect(svc.signOff).toHaveBeenCalledWith('r1', 'e1', 9);
    expect(toast.success).toHaveBeenCalledWith('Signed off Alice Tan.');
    expect(svc.team).toHaveBeenCalled();
  });

  it('a refused sign-off shows the server reason and reloads', () => {
    component.ngOnInit();
    svc.team.mockClear();
    svc.signOff.mockReturnValue(
      throwError(() => ({
        error: { error: 'The payroll run was changed by someone else. Refresh and try again.' },
      })),
    );

    component.signOff(component.lines()[0]!);

    expect(toast.error).toHaveBeenCalledWith(
      'The payroll run was changed by someone else. Refresh and try again.',
    );
    expect(svc.team).toHaveBeenCalled();
    expect(component.busy()).toBe(false);
  });

  it('shows the reason when the team cannot be loaded', () => {
    svc.teamRuns.mockReturnValue(
      throwError(() => ({ error: { error: 'You are not allowed to see this.' } })),
    );

    component.ngOnInit();

    expect(toast.error).toHaveBeenCalledWith('You are not allowed to see this.');
    expect(component.runsLoading()).toBe(false);
  });
});
