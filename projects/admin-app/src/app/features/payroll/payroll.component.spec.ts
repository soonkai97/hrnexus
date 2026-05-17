import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { PayrollComponent } from './payroll.component';
import { AdminService } from 'shared-lib';

/**
 * PayrollComponent — admin pay period summary and CSV export.
 *
 * WHY these tests matter:
 *  - load() must correctly pass year and month to the API — wrong params produce wrong pay data.
 *  - exportCsv() must clear the exporting flag on both success and error so the button
 *    is never permanently locked.
 *  - isCurrentOrFutureMonth gates a "period not closed" warning — an off formula causes
 *    managers to think a closed period is still open or vice versa.
 */

const fakePayroll = {
  year: 2026, month: 5,
  rows: [{ employeeId: 'e1', staffId: 'NC001', fullName: 'Jane Doe', totalHours: 80, totalPay: 1200, roles: [] }],
  grandTotalHours: 80, grandTotalPay: 1200,
};

describe('PayrollComponent', () => {
  let component: PayrollComponent;
  let mockAdminSvc: {
    getPayrollSummary: ReturnType<typeof vi.fn>;
    exportPayrollCsv:  ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    mockAdminSvc = {
      getPayrollSummary: vi.fn().mockReturnValue(of(fakePayroll)),
      exportPayrollCsv:  vi.fn().mockReturnValue(of(new Blob(['csv'], { type: 'text/csv' }))),
    };

    await TestBed.configureTestingModule({
      imports: [PayrollComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AdminService, useValue: mockAdminSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(PayrollComponent).componentInstance;
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  // ── Default state ────────────────────────────────────────────────────────────

  it('initialises year to the current year', () => {
    expect(component.year()).toBe(new Date().getFullYear());
  });

  it('initialises month to the current month (1-indexed)', () => {
    expect(component.month()).toBe(new Date().getMonth() + 1);
  });

  // ── load() ───────────────────────────────────────────────────────────────────

  describe('load()', () => {
    it('calls getPayrollSummary with the selected year and month', () => {
      component.year.set(2025);
      component.month.set(3);
      component.load();
      expect(mockAdminSvc.getPayrollSummary).toHaveBeenCalledWith(2025, 3);
    });

    it('populates data signal with the API response', () => {
      component.load();
      expect(component.data()).toEqual(fakePayroll);
    });

    it('clears loading flag after successful load', () => {
      component.load();
      expect(component.loading()).toBe(false);
    });

    it('clears loading flag even when the API call fails', () => {
      mockAdminSvc.getPayrollSummary.mockReturnValue(throwError(() => new Error('Network')));
      component.load();
      expect(component.loading()).toBe(false);
    });
  });

  // ── exportCsv() ──────────────────────────────────────────────────────────────

  describe('exportCsv()', () => {
    it('calls exportPayrollCsv with the selected year and month', () => {
      component.year.set(2026);
      component.month.set(5);

      // Stub DOM methods so anchor click/URL APIs don't throw in jsdom
      const createEl = vi.spyOn(document, 'createElement').mockReturnValue(
        Object.assign(document.createElement('a'), { click: vi.fn() }),
      );
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fake');
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);

      component.exportCsv();
      expect(mockAdminSvc.exportPayrollCsv).toHaveBeenCalledWith(2026, 5);
      createEl.mockRestore();
    });

    it('clears exporting flag after successful export', () => {
      vi.spyOn(document, 'createElement').mockReturnValue(
        Object.assign(document.createElement('a'), { click: vi.fn() }),
      );
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fake');
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);

      component.exportCsv();
      expect(component.exporting()).toBe(false);
    });

    it('clears exporting flag when export fails — button must not stay locked', () => {
      mockAdminSvc.exportPayrollCsv.mockReturnValue(throwError(() => new Error('Network')));
      component.exportCsv();
      expect(component.exporting()).toBe(false);
    });
  });

  // ── isCurrentOrFutureMonth computed ──────────────────────────────────────────

  describe('isCurrentOrFutureMonth computed', () => {
    const thisYear  = new Date().getFullYear();
    const thisMonth = new Date().getMonth() + 1;

    it('returns true for the current month — period is still open', () => {
      component.year.set(thisYear);
      component.month.set(thisMonth);
      expect(component.isCurrentOrFutureMonth()).toBe(true);
    });

    it('returns true for a future month', () => {
      component.year.set(thisYear + 1);
      component.month.set(1);
      expect(component.isCurrentOrFutureMonth()).toBe(true);
    });

    it('returns false for a past month — period is closed', () => {
      // Use a safely past period to avoid edge cases at month boundary
      component.year.set(2024);
      component.month.set(1);
      expect(component.isCurrentOrFutureMonth()).toBe(false);
    });

    it('returns true for a future month in the current year', () => {
      const nextMonth = thisMonth === 12 ? 1 : thisMonth + 1;
      const nextYear  = thisMonth === 12 ? thisYear + 1 : thisYear;
      component.year.set(nextYear);
      component.month.set(nextMonth);
      expect(component.isCurrentOrFutureMonth()).toBe(true);
    });
  });
});
