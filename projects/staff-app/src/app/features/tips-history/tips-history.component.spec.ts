import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { TipsHistoryComponent } from './tips-history.component';
import { PayrollService } from 'shared-lib';

/**
 * TipsHistoryComponent — staff tips earnings by month.
 *
 * WHY these tests matter:
 *  - load() constructs the date range inline — an off-by-one in the last-day
 *    formula silently excludes the final shift of the month from the tips total.
 *  - February edge cases (leap vs non-leap year) are especially important:
 *    wrong last-day means Feb 28 tips are missing for a leap year.
 *  - The local-date formula avoids toISOString() UTC shift — important for staff
 *    in KL (UTC+8) where midnight local time = previous UTC day.
 */

const fakeSummary = {
  employeeId: 'e1', periodStart: '2026-05-01', periodEnd: '2026-05-31',
  totalHours: 40, tipsAllocated: 150, grossPay: 750, shifts: [],
};

describe('TipsHistoryComponent', () => {
  let component: TipsHistoryComponent;
  let mockPayrollSvc: { getMySummary: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockPayrollSvc = { getMySummary: vi.fn().mockReturnValue(of(fakeSummary)) };

    await TestBed.configureTestingModule({
      imports: [TipsHistoryComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: PayrollService, useValue: mockPayrollSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(TipsHistoryComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Default state ─────────────────────────────────────────────────────────────

  it('initialises selectedYear to the current year', () => {
    expect(component.selectedYear()).toBe(new Date().getFullYear());
  });

  it('initialises selectedMonth to the current month (1-indexed)', () => {
    expect(component.selectedMonth()).toBe(new Date().getMonth() + 1);
  });

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('triggers load() on init so tips data is visible without a manual action', () => {
      component.ngOnInit();
      expect(mockPayrollSvc.getMySummary).toHaveBeenCalled();
    });

    it('populates the summary signal on init', () => {
      component.ngOnInit();
      expect(component.summary()).toEqual(fakeSummary);
    });
  });

  // ── load() — date range correctness ──────────────────────────────────────────

  describe('load() — date range construction', () => {
    it('passes the first day of the selected month as start date', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(5);
      component.load();
      const [start] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(start).toBe('2026-05-01');
    });

    it('passes the last day of May (31) as end date', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(5);
      component.load();
      const [, end] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(end).toBe('2026-05-31');
    });

    it('passes the last day of April (30) as end date', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(4);
      component.load();
      const [, end] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(end).toBe('2026-04-30');
    });

    it('returns 28 for February in a non-leap year — prevents missing last-day tips', () => {
      component.selectedYear.set(2025);
      component.selectedMonth.set(2);
      component.load();
      const [, end] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(end).toBe('2025-02-28');
    });

    it('returns 29 for February in a leap year (2024)', () => {
      component.selectedYear.set(2024);
      component.selectedMonth.set(2);
      component.load();
      const [, end] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(end).toBe('2024-02-29');
    });

    it('returns 31 for December', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(12);
      component.load();
      const [, end] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(end).toBe('2026-12-31');
    });

    it('zero-pads single-digit months in the start date', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(1);
      component.load();
      const [start] = mockPayrollSvc.getMySummary.mock.calls.at(-1)!;
      expect(start).toBe('2026-01-01');
    });
  });

  // ── load() — state management ─────────────────────────────────────────────────

  describe('load() — state management', () => {
    it('populates the summary signal with the API response', () => {
      component.load();
      expect(component.summary()).toEqual(fakeSummary);
    });

    it('clears the loading flag after a successful load', () => {
      component.load();
      expect(component.loading()).toBe(false);
    });

    it('clears the loading flag when the API fails — spinner must not stay stuck', () => {
      mockPayrollSvc.getMySummary.mockReturnValue(throwError(() => new Error('Network')));
      component.load();
      expect(component.loading()).toBe(false);
    });

    it('clears the summary signal on API failure', () => {
      component.summary.set(fakeSummary as any);
      mockPayrollSvc.getMySummary.mockReturnValue(throwError(() => new Error('Network')));
      component.load();
      expect(component.summary()).toBeNull();
    });
  });
});
