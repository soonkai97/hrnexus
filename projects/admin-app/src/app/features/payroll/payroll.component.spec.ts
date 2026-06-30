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

const fakeSnapshot = {
  date: '2026-05-17', month: '2026-05', totalToday: 160, totalThisMonth: 980,
  staff: [{ staffId: 'NC001', name: 'Jane Doe', earnedToday: 160, earnedThisMonth: 980 }],
};

const fakePayslip = {
  staffId: 'NC001', staffName: 'Jane Doe', from: '2026-05-01', to: '2026-05-31',
  totalHours: 12, totalPay: 740, outstandingAdvance: 200, netPayable: 540,
  jobBreakdown: [
    { role: 'PACKAGE', payType: 'fixed', shiftCount: 1, hours: 12, pay: 500 },
    { role: 'BARTENDER', payType: 'hourly', shiftCount: 1, hours: 8, pay: 240 },
  ],
  shifts: [
    { workDate: '2026-05-05', startTime: '09:00', endDate: '2026-05-05', endTime: '17:00',
      hours: 8, role: 'BARTENDER', locationName: 'Main', payType: 'hourly', rate: 30, pay: 240, isEdited: false, notes: null },
    { workDate: '2026-05-06', startTime: '20:00', endDate: '2026-05-07', endTime: '08:00',
      hours: 12, role: 'PACKAGE', locationName: 'Main', payType: 'fixed', rate: 500, pay: 500, isEdited: false, notes: null },
  ],
};

describe('PayrollComponent', () => {
  let component: PayrollComponent;
  let mockAdminSvc: {
    getPayrollSummary:   ReturnType<typeof vi.fn>;
    exportPayrollCsv:    ReturnType<typeof vi.fn>;
    getEarningsSnapshot: ReturnType<typeof vi.fn>;
    getPayslip:          ReturnType<typeof vi.fn>;
    exportPayslipCsv:    ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    mockAdminSvc = {
      getPayrollSummary:   vi.fn().mockReturnValue(of(fakePayroll)),
      exportPayrollCsv:    vi.fn().mockReturnValue(of(new Blob(['csv'], { type: 'text/csv' }))),
      getEarningsSnapshot: vi.fn().mockReturnValue(of(fakeSnapshot)),
      getPayslip:          vi.fn().mockReturnValue(of(fakePayslip)),
      exportPayslipCsv:    vi.fn().mockReturnValue(of(new Blob(['csv'], { type: 'text/csv' }))),
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

  // ── earnings snapshot ──────────────────────────────────────────────────────────

  describe('loadSnapshot()', () => {
    it('ngOnInit loads the live earned-today / this-month snapshot', () => {
      component.ngOnInit();
      expect(mockAdminSvc.getEarningsSnapshot).toHaveBeenCalled();
      expect(component.snapshot()).toEqual(fakeSnapshot);
      expect(component.snapshotLoading()).toBe(false);
    });

    it('clears the snapshot loading flag even when the API call fails', () => {
      mockAdminSvc.getEarningsSnapshot.mockReturnValue(throwError(() => new Error('Network')));
      component.loadSnapshot();
      expect(component.snapshotLoading()).toBe(false);
      expect(component.snapshot()).toBeNull();
    });
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

  // ── Payslip drill-down ───────────────────────────────────────────────────────

  describe('payslip drill-down', () => {
    const row = { staffId: 'NC001', name: 'Jane Doe', totalHours: 12, totalPay: 740 };

    it('openPayslip defaults the range to the selected month and loads it', () => {
      component.year.set(2026);
      component.month.set(5);
      component.openPayslip(row);

      expect(component.payslipOpen()).toBe(true);
      expect(component.payslipStaffId()).toBe('NC001');
      expect(component.payslipName()).toBe('Jane Doe');
      // 1st → last day of May 2026
      expect(component.payslipFrom()).toEqual(new Date(2026, 4, 1));
      expect(component.payslipTo()).toEqual(new Date(2026, 4, 31));
      expect(mockAdminSvc.getPayslip).toHaveBeenCalledWith('NC001', '2026-05-01', '2026-05-31');
      expect(component.payslip()).toEqual(fakePayslip);
    });

    it('loadPayslip sends LOCAL-formatted dates (no UTC off-by-one)', () => {
      component.payslipStaffId.set('NC001');
      component.payslipFrom.set(new Date(2026, 0, 1));   // 1 Jan 2026 local
      component.payslipTo.set(new Date(2026, 0, 31));
      component.loadPayslip();
      expect(mockAdminSvc.getPayslip).toHaveBeenCalledWith('NC001', '2026-01-01', '2026-01-31');
    });

    it('loadPayslip refuses an inverted range (to before from)', () => {
      component.payslipStaffId.set('NC001');
      component.payslipFrom.set(new Date(2026, 4, 31));
      component.payslipTo.set(new Date(2026, 4, 1));
      mockAdminSvc.getPayslip.mockClear();
      component.loadPayslip();
      expect(mockAdminSvc.getPayslip).not.toHaveBeenCalled();
    });

    it('clears the payslip loading flag even when the API call fails', () => {
      mockAdminSvc.getPayslip.mockReturnValue(throwError(() => new Error('Network')));
      component.payslipStaffId.set('NC001');
      component.payslipFrom.set(new Date(2026, 4, 1));
      component.payslipTo.set(new Date(2026, 4, 31));
      component.loadPayslip();
      expect(component.payslipLoading()).toBe(false);
    });

    it('exportPayslipCsv requests the CSV for the current range and clears the flag', () => {
      vi.spyOn(document, 'createElement').mockReturnValue(
        Object.assign(document.createElement('a'), { click: vi.fn() }),
      );
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fake');
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);

      component.payslipStaffId.set('NC001');
      component.payslipFrom.set(new Date(2026, 4, 1));
      component.payslipTo.set(new Date(2026, 4, 31));
      component.exportPayslipCsv();
      expect(mockAdminSvc.exportPayslipCsv).toHaveBeenCalledWith('NC001', '2026-05-01', '2026-05-31');
      expect(component.payslipExporting()).toBe(false);
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
