import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { PaySummaryComponent } from './pay-summary.component';
import { PayrollService, ToastService } from 'shared-lib';

/**
 * PaySummaryComponent — staff pay period summary with CSV export.
 *
 * WHY these tests matter:
 *  - periodStart/periodEnd must produce exact ISO date strings — off-by-one errors mean
 *    the API query silently excludes the first or last shift of the period.
 *  - February edge cases (leap vs non-leap year) ensure no staff member's February pay
 *    is calculated from a wrong last-day.
 *  - load() must pass the computed period dates to getMySummary — a stale date causes
 *    the staff member to see wrong data without any error message.
 */

const fakeSummary = {
  employeeId: 'e1',
  periodStart: '2026-05-01',
  periodEnd: '2026-05-31',
  totalHours: 80,
  grossPay: 1400,
  shifts: [],
};

describe('PaySummaryComponent', () => {
  let component: PaySummaryComponent;

  let mockPayrollSvc: { getMySummary: ReturnType<typeof vi.fn> };
  let mockToastSvc:  { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockPayrollSvc = { getMySummary: vi.fn().mockReturnValue(of(fakeSummary)) };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [PaySummaryComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: PayrollService, useValue: mockPayrollSvc },
        { provide: ToastService,   useValue: mockToastSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(PaySummaryComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Default state ────────────────────────────────────────────────────────────

  it('initialises selectedYear to the current year', () => {
    expect(component.selectedYear()).toBe(new Date().getFullYear());
  });

  it('initialises selectedMonth to the current month (1-indexed)', () => {
    expect(component.selectedMonth()).toBe(new Date().getMonth() + 1);
  });

  // ── periodStart computed ──────────────────────────────────────────────────────

  describe('periodStart computed', () => {
    it('returns first day of the selected month as an ISO date string', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(5);
      expect(component.periodStart()).toBe('2026-05-01');
    });

    it('zero-pads single-digit months', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(1);
      expect(component.periodStart()).toBe('2026-01-01');
    });

    it('returns correct date when year changes', () => {
      component.selectedYear.set(2025);
      component.selectedMonth.set(12);
      expect(component.periodStart()).toBe('2025-12-01');
    });
  });

  // ── periodEnd computed ────────────────────────────────────────────────────────

  describe('periodEnd computed', () => {
    it('returns last day of May (31 days)', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(5);
      expect(component.periodEnd()).toBe('2026-05-31');
    });

    it('returns last day of April (30 days)', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(4);
      expect(component.periodEnd()).toBe('2026-04-30');
    });

    it('returns 28 for February in a non-leap year — prevents wrong last-shift exclusion', () => {
      component.selectedYear.set(2025);
      component.selectedMonth.set(2);
      expect(component.periodEnd()).toBe('2025-02-28');
    });

    it('returns 29 for February in a leap year — 2024 is divisible by 4', () => {
      component.selectedYear.set(2024);
      component.selectedMonth.set(2);
      expect(component.periodEnd()).toBe('2024-02-29');
    });

    it('returns 31 for December', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(12);
      expect(component.periodEnd()).toBe('2026-12-31');
    });

    it('returns 31 for January', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(1);
      expect(component.periodEnd()).toBe('2026-01-31');
    });
  });

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('triggers load() so data is visible without a manual click', () => {
      component.ngOnInit();
      expect(mockPayrollSvc.getMySummary).toHaveBeenCalled();
    });

    it('populates the summary signal on init', () => {
      component.ngOnInit();
      expect(component.summary()).toEqual(fakeSummary);
    });
  });

  // ── load() ───────────────────────────────────────────────────────────────────

  describe('load()', () => {
    it('calls getMySummary with the computed periodStart and periodEnd', () => {
      component.selectedYear.set(2026);
      component.selectedMonth.set(5);
      component.load();
      expect(mockPayrollSvc.getMySummary).toHaveBeenCalledWith('2026-05-01', '2026-05-31');
    });

    it('calls getMySummary with correct dates after year/month change', () => {
      component.selectedYear.set(2025);
      component.selectedMonth.set(3);
      component.load();
      expect(mockPayrollSvc.getMySummary).toHaveBeenCalledWith('2025-03-01', '2025-03-31');
    });

    it('populates the summary signal with the API response', () => {
      component.load();
      expect(component.summary()).toEqual(fakeSummary);
    });

    it('clears the loading flag after a successful load', () => {
      component.load();
      expect(component.loading()).toBe(false);
    });

    it('clears the loading flag even when the API fails — button must not stay locked', () => {
      mockPayrollSvc.getMySummary.mockReturnValue(throwError(() => new Error('Network')));
      component.load();
      expect(component.loading()).toBe(false);
    });

    it('clears summary and shows error toast on API failure', () => {
      component.summary.set(fakeSummary as any);
      mockPayrollSvc.getMySummary.mockReturnValue(throwError(() => new Error('Network')));
      component.load();
      expect(component.summary()).toBeNull();
      expect(mockToastSvc.error).toHaveBeenCalledWith('pay.errLoad');
    });
  });
});
