import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  PayrollRatesService,
  PermissionService,
  ToastService,
  ConfirmService,
  StatutoryRate,
} from 'shared-lib';
import { PayrollRatesComponent } from './payroll-rates.component';

/**
 * Statutory rates page (Phase 8b).
 *
 * WHY these tests matter:
 *  - The figures here drive every payslip. A person must be able to tell a placeholder from a
 *    verified rate at a glance, so the banner and the summary text are part of the contract.
 *  - Verification is permanent and audited: it needs a written note, and only people holding
 *    payroll.rates.verify may do it. The server enforces that; the page must not even offer it.
 *  - Switching the optional HRD levy changes every payslip, so it must ask first and must show the
 *    server's reason when it is refused.
 */
describe('PayrollRatesComponent', () => {
  let component: PayrollRatesComponent;
  let svc: {
    list: ReturnType<typeof vi.fn>;
    verify: ReturnType<typeof vi.fn>;
    setActive: ReturnType<typeof vi.fn>;
  };
  let perms: { has: ReturnType<typeof vi.fn> };
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let confirmSvc: { confirm: ReturnType<typeof vi.fn> };

  const row = (over: Partial<StatutoryRate>): StatutoryRate => ({
    id: 'r',
    scheme: 'epf',
    citizenshipClass: 'citizen',
    ageFrom: 0,
    ageTo: null,
    effectiveFrom: '2020-01-01',
    kind: 'percentage',
    wageCeiling: null,
    employeeRate: 0.11,
    employerRate: 0.13,
    employerRateMode: 'flat',
    employerRateThreshold: null,
    employerRateAbove: null,
    rounding: 'none',
    amount: null,
    active: true,
    verified: false,
    verifiedBy: null,
    verifiedAt: null,
    verificationNote: null,
    source: 'PRD',
    note: null,
    bands: [],
    ...over,
  });

  const epf = row({
    id: 'epf',
    employerRateMode: 'whole-wage-by-threshold',
    employerRateThreshold: 5000,
    employerRateAbove: 0.12,
    rounding: 'up-to-next-ringgit',
  });
  const socso = row({
    id: 'socso',
    scheme: 'socso',
    employeeRate: 0.005,
    employerRate: 0.0175,
    wageCeiling: 6000,
    rounding: 'nearest-5-sen',
  });
  const hrd = row({
    id: 'hrd',
    scheme: 'hrd-optional',
    employeeRate: 0,
    employerRate: 0.005,
    rounding: 'nearest-cent',
    active: false,
  });
  const minWage = row({ id: 'mw', scheme: 'minimum-wage', kind: 'percentage', amount: 1700 });
  const bandRow = row({
    id: 'bands',
    scheme: 'socso',
    kind: 'band-table',
    bands: [
      { wageFrom: 0, wageTo: 30, employeeAmount: 0.1, employerAmount: 0.4 },
      { wageFrom: 30, wageTo: 50, employeeAmount: 0.2, employerAmount: 0.7 },
    ],
  });

  beforeEach(async () => {
    svc = {
      list: vi.fn().mockReturnValue(of([epf, socso, hrd, minWage])),
      verify: vi.fn().mockReturnValue(of(void 0)),
      setActive: vi.fn().mockReturnValue(of(void 0)),
    };
    perms = { has: vi.fn().mockReturnValue(false) };
    toast = { success: vi.fn(), error: vi.fn() };
    confirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [PayrollRatesComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PayrollRatesService, useValue: svc },
        { provide: PermissionService, useValue: perms },
        { provide: ToastService, useValue: toast },
        { provide: ConfirmService, useValue: confirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(PayrollRatesComponent).componentInstance;
  });

  describe('loading', () => {
    it('loads the rate rows on init', () => {
      component.ngOnInit();
      expect(svc.list).toHaveBeenCalled();
      expect(component.rates().length).toBe(4);
      expect(component.loading()).toBe(false);
    });

    it('toasts the failure and stops loading when the list cannot be read', () => {
      svc.list.mockReturnValue(throwError(() => ({ status: 500 })));
      component.load();
      expect(toast.error).toHaveBeenCalledWith('Failed to load statutory rates.');
      expect(component.loading()).toBe(false);
    });
  });

  describe('placeholder banner', () => {
    it('shows while any row is unverified', () => {
      component.rates.set([row({ verified: true }), row({ id: 'x', verified: false })]);
      expect(component.anyUnverified()).toBe(true);
    });

    it('is hidden once every row is verified', () => {
      component.rates.set([row({ verified: true }), row({ id: 'x', verified: true })]);
      expect(component.anyUnverified()).toBe(false);
    });

    it('is hidden when there are no rows', () => {
      component.rates.set([]);
      expect(component.anyUnverified()).toBe(false);
    });
  });

  describe('labels', () => {
    it('names each scheme the way an accountant would', () => {
      expect(component.schemeLabel(epf)).toBe('EPF');
      expect(component.schemeLabel(socso)).toBe('SOCSO (category 1)');
      expect(component.schemeLabel(row({ scheme: 'eis' }))).toBe('EIS');
      expect(component.schemeLabel(row({ scheme: 'hrd-compulsory' }))).toBe('HRD Corp levy 1%');
      expect(component.schemeLabel(hrd)).toBe('HRD Corp levy 0.5% (optional)');
      expect(component.schemeLabel(minWage)).toBe('Minimum wage');
    });

    it('names the citizenship class', () => {
      expect(component.classLabel(epf)).toBe('Citizen');
      expect(component.classLabel(row({ citizenshipClass: 'permanent-resident' }))).toBe(
        'Permanent resident',
      );
    });
  });

  describe('rate summary', () => {
    it('EPF: both shares, the higher-wage employer rate and the rounding', () => {
      expect(component.ratesSummary(epf)).toBe(
        'Employee 11% · Employer 13% (12% above RM5,000) · rounded up to the next ringgit',
      );
    });

    it('SOCSO: shares in fractions of a percent, the wage ceiling and 5 sen rounding', () => {
      expect(component.ratesSummary(socso)).toBe(
        'Employee 0.5% · Employer 1.75% · wages up to RM6,000 · rounded to the nearest 5 sen',
      );
    });

    it('HRD: only the employer share is shown, rounded to the nearest sen', () => {
      expect(component.ratesSummary(hrd)).toBe('Employer 0.5% · rounded to the nearest sen');
    });

    it('minimum wage: the monthly amount', () => {
      expect(component.ratesSummary(minWage)).toBe('RM1,700 a month');
    });

    it('band table: the number of wage bands', () => {
      expect(component.ratesSummary(bandRow)).toBe('2 wage bands');
      expect(component.ratesSummary({ ...bandRow, bands: [bandRow.bands[0]] })).toBe('1 wage band');
    });

    it('shows no rounding text for a rule that does not round', () => {
      expect(component.ratesSummary(row({ employerRate: 0.13 }))).toBe(
        'Employee 11% · Employer 13%',
      );
    });
  });

  describe('band detail', () => {
    it('expands and collapses a band table row', () => {
      expect(component.isExpanded('bands')).toBe(false);
      component.toggleBands('bands');
      expect(component.isExpanded('bands')).toBe(true);
      component.toggleBands('bands');
      expect(component.isExpanded('bands')).toBe(false);
    });

    it('formats band money with two decimals', () => {
      expect(component.money(0.4)).toBe('0.40');
      expect(component.money(1700)).toBe('1,700.00');
    });
  });

  describe('verified status', () => {
    it('tells who verified a row, when, and against what', () => {
      const text = component.verifiedTitle(
        row({
          verified: true,
          verifiedBy: 'Alice',
          verifiedAt: '2026-11-14T02:00:00Z',
          verificationNote: 'KWSP third schedule',
        }),
      );
      expect(text).toBe('by Alice on 14/11/2026: KWSP third schedule');
    });

    it('recognises the two HRD schemes only', () => {
      expect(component.isHrd(hrd)).toBe(true);
      expect(component.isHrd(row({ scheme: 'hrd-compulsory' }))).toBe(true);
      expect(component.isHrd(epf)).toBe(false);
    });
  });

  describe('permission', () => {
    it('canVerify is false without payroll.rates.verify (read-only page)', () => {
      perms.has.mockImplementation((k: string) => k === 'payroll.view');
      expect(component.canVerify).toBe(false);
    });

    it('canVerify is true with payroll.rates.verify', () => {
      perms.has.mockImplementation((k: string) => k === 'payroll.rates.verify');
      expect(component.canVerify).toBe(true);
    });
  });

  describe('verifying a row', () => {
    it('opens the dialog for the chosen row with an empty note', () => {
      component.verifyNote = 'old';
      component.openVerify(epf);
      expect(component.verifyTarget()).toBe(epf);
      expect(component.verifyNote).toBe('');
    });

    it('does not call the server for a blank note', () => {
      component.openVerify(epf);
      component.verifyNote = '   ';
      expect(component.isVerifyValid).toBe(false);
      component.confirmVerify();
      expect(svc.verify).not.toHaveBeenCalled();
    });

    it('sends the trimmed note, toasts and reloads', () => {
      component.openVerify(epf);
      component.verifyNote = '  Checked against the KWSP table  ';
      component.confirmVerify();
      expect(svc.verify).toHaveBeenCalledWith('epf', 'Checked against the KWSP table');
      expect(toast.success).toHaveBeenCalledWith('Rate marked as verified.');
      expect(svc.list).toHaveBeenCalled();
      expect(component.verifyTarget()).toBeNull();
    });

    it('shows the server message and keeps the dialog open when verifying is refused', () => {
      svc.verify.mockReturnValue(throwError(() => ({ error: { error: 'Already verified.' } })));
      component.openVerify(epf);
      component.verifyNote = 'note';
      component.confirmVerify();
      expect(toast.error).toHaveBeenCalledWith('Already verified.');
      expect(component.verifyTarget()).toBe(epf);
      expect(component.saving()).toBe(false);
    });

    it('cancel closes the dialog without calling the server', () => {
      component.openVerify(epf);
      component.cancelVerify();
      expect(component.verifyTarget()).toBeNull();
      expect(svc.verify).not.toHaveBeenCalled();
    });
  });

  describe('switching the optional HRD levy', () => {
    it('asks first, then switches it on and reloads', () => {
      component.toggleActive(hrd);
      expect(confirmSvc.confirm).toHaveBeenCalled();
      expect(svc.setActive).toHaveBeenCalledWith('hrd', true);
      expect(toast.success).toHaveBeenCalled();
      expect(svc.list).toHaveBeenCalled();
    });

    it('switches an active row off', () => {
      component.toggleActive({ ...hrd, active: true });
      expect(svc.setActive).toHaveBeenCalledWith('hrd', false);
    });

    it('does nothing when the confirmation is declined', () => {
      confirmSvc.confirm.mockReturnValue(of(false));
      component.toggleActive(hrd);
      expect(svc.setActive).not.toHaveBeenCalled();
    });

    it('shows the server message when another HRD row is already on (409)', () => {
      svc.setActive.mockReturnValue(
        throwError(() => ({
          status: 409,
          error: { error: 'Another HRD Corp row is already on.' },
        })),
      );
      component.toggleActive(hrd);
      expect(toast.error).toHaveBeenCalledWith('Another HRD Corp row is already on.');
    });
  });

  // ── Phase 7r: card view ─────────────────────────────────────────────────────

  it('starts on the list (table) view, and cards are one click away', () => {
    expect(component.viewMode()).toBe('table');
  });
});
