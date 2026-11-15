import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { PayrollRunService } from '../services/payroll-run.service';
import { ToastService } from '../services/toast.service';
import {
  PayrollRunDetail,
  PayrollRunLine,
  PayrollTeam,
  PayrollTeamRun,
} from '../models/payroll-run.model';
import { PayrollRunStore } from './payroll-run.store';

/**
 * The payroll run store: what every payroll page reads and does.
 *
 * WHY these tests matter: pages never call the API, so the store is where the safety rules live. Every action must send the
 * version the store last read (a stale page is refused); a refusal must say why and read the run again; only one action runs at a
 * time; an answer for a run or tab the person already left must never replace the one on screen; and what a person may do comes
 * from the server, never from the store.
 */
describe('PayrollRunStore', () => {
  let store: PayrollRunStore;
  let svc: Record<string, ReturnType<typeof vi.fn>>;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

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
    canSignOff: true,
    ...over,
  });

  const detail = (
    id = 'r1',
    version = 7,
    over: Partial<PayrollRunDetail> = {},
  ): PayrollRunDetail => ({
    summary: {
      id,
      year: 2026,
      month: 10,
      state: 'submitted',
      version,
      people: 1,
      leftOut: 0,
      signedOff: 0,
      totalGross: 3000,
      totalNet: 2500,
      totalEmployerCost: 3300,
      createdAt: 'x',
      exportedAt: null,
    },
    calculatorVersion: '1',
    divisor: 26,
    hoursPerWorkingDay: 8,
    deductionCapRate: 0.5,
    lines: [line()],
    steps: [],
    unverifiedRates: [],
    notInRun: [],
    allowedActions: ['signoff-all'],
    ...over,
  });

  const team = (runId = 'r1', version = 9): PayrollTeam => ({
    runId,
    year: 2026,
    month: 10,
    state: 'submitted',
    version,
    lines: [line()],
  });
  const teamRun = (runId: string, toSign = 1): PayrollTeamRun => ({
    runId,
    year: 2026,
    month: 10,
    state: 'submitted',
    lines: 1,
    toSign,
  });

  beforeEach(() => {
    svc = {
      list: vi.fn().mockReturnValue(of([])),
      get: vi.fn().mockReturnValue(of(detail())),
      start: vi.fn().mockReturnValue(of({ id: 'new' })),
      delete: vi.fn().mockReturnValue(of(void 0)),
      recalculate: vi.fn().mockReturnValue(of({ version: 8 })),
      updateLine: vi.fn().mockReturnValue(of({ version: 8 })),
      submit: vi.fn().mockReturnValue(of({ version: 8 })),
      signOff: vi.fn().mockReturnValue(of({ version: 8 })),
      signOffAll: vi.fn().mockReturnValue(of({ signed: 2 })),
      returnRun: vi.fn().mockReturnValue(of({ version: 8 })),
      approve: vi.fn().mockReturnValue(of({ version: 8 })),
      lock: vi.fn().mockReturnValue(of(void 0)),
      reopen: vi.fn().mockReturnValue(of(void 0)),
      teamRuns: vi.fn().mockReturnValue(of([teamRun('r1')])),
      team: vi.fn().mockReturnValue(of(team())),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        { provide: PayrollRunService, useValue: svc },
        { provide: ToastService, useValue: toast },
      ],
    });
    store = TestBed.inject(PayrollRunStore);
  });

  // ── what is offered comes from the server ─────────────────────────────────────

  it('offers exactly the actions the server lists for the open run, and none before a run is open', () => {
    expect(store.can('signoff-all')).toBe(false);

    store.open('r1');

    expect(store.can('signoff-all')).toBe(true);
    expect(store.can('approve')).toBe(false);
  });

  // ── reading ───────────────────────────────────────────────────────────────────

  it('reads the list, and says why when it cannot', () => {
    svc['list'].mockReturnValue(of([{ id: 'r1' }]));
    store.loadRuns();
    expect(store.runs()).toHaveLength(1);
    expect(store.runsLoading()).toBe(false);

    svc['list'].mockReturnValue(throwError(() => ({ error: { error: 'Not allowed.' } })));
    store.loadRuns();
    expect(toast.error).toHaveBeenCalledWith('Not allowed.');
    expect(store.runsLoading()).toBe(false);
  });

  it('opens a run asking who cannot be in a draft yet, and forgets it on close', () => {
    store.open('r1');
    expect(svc['get']).toHaveBeenCalledWith('r1', true);
    expect(store.summary()?.id).toBe('r1');

    store.close();
    expect(store.detail()).toBeNull();
  });

  it('never shows the previous run while the next one loads', () => {
    store.open('r1');
    svc['get'].mockReturnValue(new Subject());

    store.open('r2');

    expect(store.detail()).toBeNull();
  });

  it('drops a slow answer for a run the person already left', () => {
    const slow = new Subject<PayrollRunDetail>();
    svc['get'].mockReturnValue(slow);
    store.open('r1');
    store.close();

    slow.next(detail('r1'));

    expect(store.detail()).toBeNull();
  });

  // ── actions send the version the store read ───────────────────────────────────

  it('every action sends the version it last read, then reads the run again', () => {
    store.open('r1');
    svc['get'].mockClear();

    store.submit().subscribe();
    store.recalculate().subscribe();
    store.approve().subscribe();
    store.lock().subscribe();
    store.reopen().subscribe();
    store.returnRun('PCB missing').subscribe();
    store.signOff(line()).subscribe();
    store.signOffAll().subscribe();
    store.updateLine('e1', { pcb: 5, excluded: false, excludedReason: null }).subscribe();

    expect(svc['submit']).toHaveBeenCalledWith('r1', 7);
    expect(svc['recalculate']).toHaveBeenCalledWith('r1', 7);
    expect(svc['approve']).toHaveBeenCalledWith('r1', 7);
    expect(svc['lock']).toHaveBeenCalledWith('r1', 7);
    expect(svc['reopen']).toHaveBeenCalledWith('r1', 7);
    expect(svc['returnRun']).toHaveBeenCalledWith('r1', 'PCB missing', 7);
    expect(svc['signOff']).toHaveBeenCalledWith('r1', 'e1', 7);
    expect(svc['signOffAll']).toHaveBeenCalledWith('r1', 7);
    expect(svc['updateLine']).toHaveBeenCalledWith('r1', 'e1', {
      pcb: 5,
      excluded: false,
      excludedReason: null,
      expectedVersion: 7,
    });
    expect(svc['get']).toHaveBeenCalledTimes(9);
  });

  it('an action emits once on success, so a page can close its dialog', () => {
    store.open('r1');
    const done = vi.fn();

    store.submit().subscribe(done);

    expect(done).toHaveBeenCalledTimes(1);
    expect(toast.success).toHaveBeenCalledWith('Submitted for sign-off.');
  });

  it('a refused action says why, reads the run again and completes without emitting', () => {
    store.open('r1');
    svc['submit'].mockReturnValue(
      throwError(() => ({
        error: { error: 'The payroll run was changed by someone else. Refresh and try again.' },
      })),
    );
    svc['get'].mockClear();
    const done = vi.fn();
    const complete = vi.fn();

    store.submit().subscribe({ next: done, complete });

    expect(toast.error).toHaveBeenCalledWith(
      'The payroll run was changed by someone else. Refresh and try again.',
    );
    expect(svc['get']).toHaveBeenCalled();
    expect(done).not.toHaveBeenCalled();
    expect(complete).toHaveBeenCalled();
    expect(store.busy()).toBe(false);
  });

  it('runs one action at a time', () => {
    store.open('r1');
    const sent = vi.fn();
    svc['submit'].mockReturnValue(new Observable(() => sent())); // never answers
    store.submit().subscribe();

    store.recalculate().subscribe();

    expect(sent).toHaveBeenCalledTimes(1);
    expect(svc['recalculate']).not.toHaveBeenCalled();
    expect(store.busy()).toBe(true);
  });

  it('says how many lines sign off all signed, and says so when nothing was left', () => {
    store.open('r1');
    store.signOffAll().subscribe();
    expect(toast.success).toHaveBeenCalledWith('Signed off 2 lines.');

    svc['signOffAll'].mockReturnValue(of({ signed: 1 }));
    store.signOffAll().subscribe();
    expect(toast.success).toHaveBeenCalledWith('Signed off 1 line.');

    svc['signOffAll'].mockReturnValue(of({ signed: 0 }));
    store.signOffAll().subscribe();
    expect(toast.success).toHaveBeenCalledWith('Nothing was left for you to sign off.');
  });

  it('deleting a draft forgets it and does not read the deleted run again', () => {
    store.open('r1');
    svc['get'].mockClear();

    store.remove().subscribe();

    expect(svc['delete']).toHaveBeenCalledWith('r1');
    expect(svc['get']).not.toHaveBeenCalled();
    expect(store.detail()).toBeNull();
  });

  it('a refused delete reads the run again, since the run still exists', () => {
    store.open('r1');
    svc['delete'].mockReturnValue(throwError(() => ({ error: { error: 'No.' } })));
    svc['get'].mockClear();

    store.remove().subscribe();

    expect(svc['get']).toHaveBeenCalled();
    expect(store.detail()).not.toBeNull();
  });

  it('starts a month and emits the new run id', () => {
    const id = vi.fn();

    store.start(2026, 10).subscribe(id);

    expect(svc['start']).toHaveBeenCalledWith(2026, 10);
    expect(id).toHaveBeenCalledWith('new');
  });

  it('a refused start says why and emits nothing', () => {
    svc['start'].mockReturnValue(throwError(() => ({ error: { error: 'Another run is open.' } })));
    const id = vi.fn();

    store.start(2026, 10).subscribe(id);

    expect(toast.error).toHaveBeenCalledWith('Another run is open.');
    expect(id).not.toHaveBeenCalled();
  });

  // ── a manager's view ──────────────────────────────────────────────────────────

  it('opens the run waiting for this manager first, otherwise the newest', () => {
    svc['teamRuns'].mockReturnValue(of([teamRun('new', 0), teamRun('waiting', 3)]));
    store.loadTeamRuns();
    expect(store.teamId()).toBe('waiting');
    expect(svc['team']).toHaveBeenCalledWith('waiting');
  });

  it('shows nothing selected when there are no runs for this manager', () => {
    svc['teamRuns'].mockReturnValue(of([]));

    store.loadTeamRuns();

    expect(store.teamId()).toBeNull();
    expect(svc['team']).not.toHaveBeenCalled();
  });

  /** WHY: sign-off acts on the run on screen; a slow answer for a tab the manager left must never replace the month they are looking at. */
  it('ignores a slow team answer for a tab the manager already left', () => {
    const oct = new Subject<PayrollTeam>();
    const sep = new Subject<PayrollTeam>();
    svc['team'].mockImplementation((id: string) => (id === 'oct' ? oct : sep));
    store.selectTeamRun('oct');

    store.selectTeamRun('sep');
    sep.next(team('sep'));
    oct.next(team('oct')); // arrives last

    expect(store.team()?.runId).toBe('sep');
  });

  it('clears the old month when the manager switches tabs', () => {
    store.selectTeamRun('r1');
    svc['team'].mockReturnValue(new Subject());

    store.selectTeamRun('other');

    expect(store.team()).toBeNull();
  });

  it('a manager sign-off sends the version of the team view, then refreshes the view and the badges', () => {
    store.selectTeamRun('r1');
    svc['team'].mockClear();
    svc['teamRuns'].mockClear();
    svc['teamRuns'].mockReturnValue(of([teamRun('r1', 0)]));

    store.signOffTeamLine(line()).subscribe();

    expect(svc['signOff']).toHaveBeenCalledWith('r1', 'e1', 9);
    expect(svc['team']).toHaveBeenCalled();
    expect(store.teamRuns()[0]!.toSign).toBe(0);
  });

  it('a refused manager sign-off says why and reads the view again', () => {
    store.selectTeamRun('r1');
    svc['signOff'].mockReturnValue(throwError(() => ({ error: { error: 'Changed.' } })));
    svc['team'].mockClear();

    store.signOffTeamLine(line()).subscribe();

    expect(toast.error).toHaveBeenCalledWith('Changed.');
    expect(svc['team']).toHaveBeenCalled();
    expect(store.busy()).toBe(false);
  });
});
