import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  ConfirmService,
  PayrollRunAction,
  PayrollRunDetail,
  PayrollRunLine,
  PayrollRunService,
  PayrollRunStore,
  ToastService,
} from 'shared-lib';
import { PayrollRunDetailComponent } from './payroll-run-detail.component';

/**
 * One month's payroll run page (through PayrollRunStore; only the HTTP service is mocked).
 *
 * WHY these tests matter: this page is where pay is prepared, signed off, approved and locked. A button appears only when the
 * server says the signed-in person may press it (`allowedActions`, `canSignOff`); the page never works out who may do what. Every
 * action must send the version the page read (so a stale page is refused), and a refusal must show the server's reason and
 * reload, never leave the page showing old figures. Who may do what is decided and tested on the server.
 */
describe('PayrollRunDetailComponent', () => {
  let component: PayrollRunDetailComponent;
  let store: PayrollRunStore;
  let svc: Record<string, ReturnType<typeof vi.fn>>;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let confirmSvc: { confirm: ReturnType<typeof vi.fn> };

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
    canSignOff: false,
    ...over,
  });

  const detail = (
    state: PayrollRunDetail['summary']['state'],
    lines: PayrollRunLine[] = [line()],
    over: Partial<PayrollRunDetail> = {},
  ): PayrollRunDetail => ({
    summary: {
      id: 'r1',
      year: 2026,
      month: 10,
      state,
      version: 7,
      people: lines.filter((l) => !l.excluded).length,
      leftOut: 0,
      signedOff: lines.filter((l) => l.signedOffBy).length,
      totalGross: 3000,
      totalNet: 2500,
      totalEmployerCost: 3300,
      createdAt: '2026-11-01T00:00:00Z',
      exportedAt: null,
    },
    calculatorVersion: '1',
    divisor: 26,
    hoursPerWorkingDay: 8,
    deductionCapRate: 0.5,
    lines,
    steps: [
      { action: 'started', staffId: 'p', name: 'Prep', at: '2026-11-01T00:00:00Z', reason: null },
    ],
    unverifiedRates: [],
    notInRun: [],
    allowedActions: [],
    ...over,
  });

  /** Opens the page on this answer from the server. */
  function load(d: PayrollRunDetail): void {
    svc['get'].mockReturnValue(of(d));
    component.ngOnInit();
  }

  beforeEach(async () => {
    svc = {
      get: vi.fn().mockReturnValue(of(detail('draft'))),
      recalculate: vi.fn().mockReturnValue(of({ version: 8 })),
      updateLine: vi.fn().mockReturnValue(of({ version: 8 })),
      submit: vi.fn().mockReturnValue(of({ version: 8 })),
      signOff: vi.fn().mockReturnValue(of({ version: 8 })),
      signOffAll: vi.fn().mockReturnValue(of({ signed: 2 })),
      returnRun: vi.fn().mockReturnValue(of({ version: 8 })),
      approve: vi.fn().mockReturnValue(of({ version: 8 })),
      lock: vi.fn().mockReturnValue(of(void 0)),
      reopen: vi.fn().mockReturnValue(of(void 0)),
      delete: vi.fn().mockReturnValue(of(void 0)),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    confirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [PayrollRunDetailComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PayrollRunService, useValue: svc },
        { provide: ToastService, useValue: toast },
        { provide: ConfirmService, useValue: confirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    store = TestBed.inject(PayrollRunStore);
    component = TestBed.createComponent(PayrollRunDetailComponent).componentInstance;
    component.id = 'r1';
  });

  it('opens the run, asking the server who cannot be in a draft yet', () => {
    component.ngOnInit();

    expect(svc['get']).toHaveBeenCalledWith('r1', true);
    expect(component.summary()?.state).toBe('draft');
    expect(component.loading()).toBe(false);
  });

  it('shows the server reason when the run cannot be loaded', () => {
    svc['get'].mockReturnValue(throwError(() => ({ error: { error: 'Payroll run not found.' } })));

    component.ngOnInit();

    expect(toast.error).toHaveBeenCalledWith('Payroll run not found.');
  });

  it('forgets the run when the page is left, so the next page never starts from its figures', () => {
    component.ngOnInit();

    component.ngOnDestroy();

    expect(component.detail()).toBeNull();
  });

  // ── what is offered: only what the server allows ──────────────────────────────

  /** WHY: who may do what is a server rule (permission, state, who prepared the run, whose pay it is); the page must not re-decide it. */
  it('draws exactly the actions the server allows', () => {
    const allowed: PayrollRunAction[] = ['signoff-all', 'return'];
    load(detail('submitted', [line()], { allowedActions: allowed }));

    expect(component.can('signoff-all')).toBe(true);
    expect(component.can('return')).toBe(true);
    expect(component.can('approve')).toBe(false);
    expect(component.can('lock')).toBe(false);
  });

  it('a line carries its own sign-off permission from the server', () => {
    load(
      detail('submitted', [
        line({ canSignOff: true }),
        line({ employeeId: 'e2', canSignOff: false }),
      ]),
    );

    expect(component.lines().map((l) => l.canSignOff)).toEqual([true, false]);
  });

  it('Approve waits until every included line is signed; a line that is left out needs no signature', () => {
    load(
      detail('submitted', [
        line({ signedOffBy: 'm', signedOffByName: 'M' }),
        line({ employeeId: 'x', excluded: true, excludedReason: 'away' }),
      ]),
    );
    expect(component.allSigned()).toBe(true);

    load(
      detail('submitted', [
        line({ signedOffBy: 'm', signedOffByName: 'M' }),
        line({ employeeId: 'y' }),
      ]),
    );
    expect(component.allSigned()).toBe(false);
    expect(component.waiting()).toHaveLength(1);
  });

  it('lists the unverified rates the run rests on so a refused approval is explained before it is tried', () => {
    load(detail('submitted', [line()], { unverifiedRates: ['EPF (citizen)'] }));

    expect(component.unverified()).toEqual(['EPF (citizen)']);
  });

  // ── actions send the version the page read ────────────────────────────────────

  it('submit, recalculate and approve send the version the page read, then reload', () => {
    load(detail('draft'));
    component.submit();
    expect(svc['submit']).toHaveBeenCalledWith('r1', 7);

    component.recalculate();
    expect(svc['recalculate']).toHaveBeenCalledWith('r1', 7);

    load(detail('submitted', [line({ signedOffBy: 'm', signedOffByName: 'M' })]));
    svc['get'].mockClear();
    component.approve();
    expect(confirmSvc.confirm).toHaveBeenCalled();
    expect(svc['approve']).toHaveBeenCalledWith('r1', 7);
    expect(toast.success).toHaveBeenCalledWith('Approved.');
    expect(svc['get']).toHaveBeenCalled();
  });

  it('approve does nothing when the person says no to the confirmation', () => {
    load(detail('submitted'));
    confirmSvc.confirm.mockReturnValue(of(false));

    component.approve();

    expect(svc['approve']).not.toHaveBeenCalled();
  });

  it('a refused action shows the server reason and reloads so the page is current again', () => {
    load(detail('draft'));
    svc['submit'].mockReturnValue(
      throwError(() => ({
        error: { error: 'The payroll run was changed by someone else. Refresh and try again.' },
      })),
    );
    svc['get'].mockClear();

    component.submit();

    expect(toast.error).toHaveBeenCalledWith(
      'The payroll run was changed by someone else. Refresh and try again.',
    );
    expect(svc['get']).toHaveBeenCalled();
    expect(component.busy()).toBe(false);
  });

  it('only one action runs at a time', () => {
    load(detail('draft'));
    const sent = vi.fn();
    svc['submit'].mockReturnValue(new Observable(() => sent())); // a request only goes out when it is subscribed to
    component.submit(); // now busy: the request never answers

    component.recalculate();

    expect(sent).toHaveBeenCalledTimes(1);
    expect(svc['recalculate']).not.toHaveBeenCalled();
  });

  it('sign-off sends the person and the version, then closes their panel', () => {
    load(detail('submitted', [line({ canSignOff: true })]));
    const l = component.lines()[0]!;
    component.openLine(l);
    expect(component.selected()).not.toBeNull();

    component.signOff(l);

    expect(svc['signOff']).toHaveBeenCalledWith('r1', 'e1', 7);
    expect(component.selected()).toBeNull();
  });

  it('after a refusal the open person dialog shows the line as it is now', () => {
    load(detail('submitted', [line({ canSignOff: true })]));
    component.openLine(component.lines()[0]!);
    svc['signOff'].mockReturnValue(throwError(() => ({ error: { error: 'Already signed.' } })));
    svc['get'].mockReturnValue(
      of(detail('submitted', [line({ signedOffBy: 'm', signedOffByName: 'Manager' })])),
    );

    component.signOff(component.lines()[0]!);

    expect(component.selected()?.signedOffBy).toBe('m');
    expect(component.selected()?.canSignOff).toBe(false);
  });

  it('sign off all says how many lines it signed, and says so when nothing was left', () => {
    load(detail('submitted'));
    component.signOffAll();
    expect(toast.success).toHaveBeenCalledWith('Signed off 2 lines.');

    svc['signOffAll'].mockReturnValue(of({ signed: 0 }));
    component.signOffAll();
    expect(toast.success).toHaveBeenCalledWith('Nothing was left for you to sign off.');
  });

  it('return needs a reason and sends it with the version', () => {
    load(detail('submitted'));
    component.openReturn();
    component.returnReason = '   ';
    component.confirmReturn();
    expect(svc['returnRun']).not.toHaveBeenCalled();

    component.returnReason = ' PCB missing ';
    component.confirmReturn();
    expect(svc['returnRun']).toHaveBeenCalledWith('r1', 'PCB missing', 7);
    expect(component.showReturn()).toBe(false);
  });

  it('lock and reopen ask first and send the version', () => {
    load(detail('approved'));
    component.lock();
    expect(svc['lock']).toHaveBeenCalledWith('r1', 7);

    confirmSvc.confirm.mockReturnValue(of(false));
    component.reopen();
    expect(svc['reopen']).not.toHaveBeenCalled();

    confirmSvc.confirm.mockReturnValue(of(true));
    component.reopen();
    expect(svc['reopen']).toHaveBeenCalledWith('r1', 7);
  });

  it('deleting a draft leaves for the list', () => {
    load(detail('draft'));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.deleteRun();

    expect(svc['delete']).toHaveBeenCalledWith('r1');
    expect(navigate).toHaveBeenCalledWith(['/payroll-runs']);
  });

  it('deleting a draft does not load the run it just deleted (that would only say "not found")', () => {
    load(detail('draft'));
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    svc['get'].mockClear();

    component.deleteRun();

    expect(svc['get']).not.toHaveBeenCalled();
    expect(store.detail()).toBeNull();
  });

  it('leaving a person out needs a reason, and a negative PCB is refused', () => {
    load(detail('draft', [line()], { allowedActions: ['edit-lines'] }));
    component.openLine(component.lines()[0]!);

    component.editExcluded = true;
    component.editReason = ' ';
    expect(component.isLineValid).toBe(false);
    component.editReason = 'Final pay handled separately';
    expect(component.isLineValid).toBe(true);
    component.editExcluded = false;
    component.editPcb = -5;
    expect(component.isLineValid).toBe(false);
  });

  it('saving a person sends their inputs with the version', () => {
    load(detail('draft', [line()], { allowedActions: ['edit-lines'] }));
    component.openLine(component.lines()[0]!);
    component.editPcb = 120;

    component.saveLine();

    expect(svc['updateLine']).toHaveBeenCalledWith('r1', 'e1', {
      pcb: 120,
      excluded: false,
      excludedReason: null,
      expectedVersion: 7,
    });
  });

  it("groups a person's payslip lines by section in payslip order and drops empty sections", () => {
    const l = line({
      items: [
        {
          type: 'epf-employee',
          section: 'EmployeeStatutory',
          description: 'EPF employee',
          amount: 330,
        },
        { type: 'basic', section: 'Earning', description: 'Basic salary', amount: 3000 },
      ],
    });

    expect(component.groups(l).map((g) => g.label)).toEqual(['Earnings', 'Statutory (employee)']);
  });

  it('an unverified rate is a note, not something to fix before submitting', () => {
    const l = line({
      warnings: [
        {
          code: 'unverified-rate',
          message: 'The EPF rate row used has not been verified.',
          blocking: true,
        },
        { code: 'no-date-of-birth', message: 'No date of birth.', blocking: true },
      ],
    });

    expect(component.mustFix(l.warnings[0]!)).toBe(false);
    expect(component.mustFix(l.warnings[1]!)).toBe(true);
    expect([component.blockingCount(l), component.noteCount(l)]).toEqual([1, 1]);
  });

  it('counts warnings that must be fixed apart from notes', () => {
    const l = line({
      warnings: [
        { code: 'a', message: 'm', blocking: true },
        { code: 'b', message: 'm', blocking: false },
        { code: 'c', message: 'm', blocking: false },
      ],
    });

    expect([component.blockingCount(l), component.noteCount(l)]).toEqual([1, 2]);
  });
});
