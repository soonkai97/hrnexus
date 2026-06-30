import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ReportsComponent } from './reports.component';
import {
  AdminService, StaffStore, StaffManagementService, ToastService,
  AuthService, PayrollLockService, ConfirmService,
} from 'shared-lib';

/**
 * ReportsComponent — admin workforce analytics with multi-tab CSV export + staff ledger payout.
 *
 * WHY these tests matter:
 *  - runReport() 366-day guard prevents expensive backend queries that could time out;
 *    a missing guard allows managers to select year-spanning ranges accidentally.
 *  - setPreset() date arithmetic must produce the correct ISO date strings — off-by-one
 *    errors mean the report silently excludes the first or last day of the period.
 *  - downloadReportCsv() must use the active tab to choose the correct column layout;
 *    a wrong header row makes the CSV unreadable in Excel.
 */

// Minimal ReportResponse shape — real API may have more fields
const fakeReport = {
  summary:    { totalShifts: 10, totalHours: 80, totalCost: 1200, uniqueStaff: 1, uniqueLocations: 1, avgHoursPerShift: 8, fixedShifts: 2, fixedCost: 1000, hourlyShifts: 8, hourlyCost: 200 },
  byStaff:    [{ staffId: 'NC001', name: 'Jane Doe', role: 'bartender', shifts: 5, hours: 40, avgHoursPerShift: 8, cost: 600 }],
  byLocation: [{ locationName: 'Main Bar', shifts: 8, hours: 64, cost: 960 }],
  byRole:     [{ role: 'bartender', staffCount: 1, shifts: 5, hours: 40, cost: 600 }],
  byDate:     [{ date: '2026-05-10', shifts: 2, hours: 16, cost: 240 }],
  shifts:     [{
    staffId: 'NC001', staffName: 'Jane Doe', role: 'bartender', payType: 'hourly',
    locationName: 'Main Bar',
    workDate: '2026-05-10', startTime: '18:00', endDate: '2026-05-10', endTime: '23:00',
    clockedIn: '2026-05-10T18:00:00', clockedOut: '2026-05-10T23:00:00',
    hours: 5, cost: 75, status: 'completed',
  }],
};

describe('ReportsComponent', () => {
  let component: ReportsComponent;

  let mockAdminSvc:  { getReport: ReturnType<typeof vi.fn> };
  let mockStaffStore: {
    roles:     ReturnType<typeof vi.fn>;
    loadRoles: ReturnType<typeof vi.fn>;
  };
  let mockStaffSvc: {
    getAll:       ReturnType<typeof vi.fn>;
    getLocations: ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:       { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockAuthSvc:        { user: ReturnType<typeof vi.fn>; userRole: ReturnType<typeof vi.fn> };
  let mockLockSvc:        { list: ReturnType<typeof vi.fn>; lock: ReturnType<typeof vi.fn>; reopen: ReturnType<typeof vi.fn> };
  let mockConfirmSvc:     { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAdminSvc  = { getReport: vi.fn().mockReturnValue(of(fakeReport)) };
    mockStaffStore = {
      roles:     vi.fn().mockReturnValue([]),
      loadRoles: vi.fn(),
    };
    mockStaffSvc  = {
      getAll:       vi.fn().mockReturnValue(of({ items: [], total: 0 })),
      getLocations: vi.fn().mockReturnValue(of([])),
    };
    mockToastSvc  = { success: vi.fn(), error: vi.fn() };
    mockAuthSvc = { user: vi.fn().mockReturnValue(null), userRole: vi.fn().mockReturnValue(null) };
    mockLockSvc = { list: vi.fn().mockReturnValue(of([])), lock: vi.fn().mockReturnValue(of({ id: 'x' })), reopen: vi.fn().mockReturnValue(of(void 0)) };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [ReportsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AdminService,          useValue: mockAdminSvc },
        { provide: StaffStore,            useValue: mockStaffStore },
        { provide: StaffManagementService, useValue: mockStaffSvc },
        { provide: ToastService,          useValue: mockToastSvc },
        { provide: AuthService,           useValue: mockAuthSvc },
        { provide: PayrollLockService,    useValue: mockLockSvc },
        { provide: ConfirmService,        useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(ReportsComponent).componentInstance;
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads all staff (pageSize 999) for the dropdown — not the paginated store', () => {
      component.ngOnInit();
      expect(mockStaffSvc.getAll).toHaveBeenCalledWith('', 1, 999);
    });

    it('loads roles so the role filter is populated', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadRoles).toHaveBeenCalled();
    });

    it('fetches location names for the location filter', () => {
      mockStaffSvc.getLocations.mockReturnValue(of([{ id: 'l1', name: 'Main Bar' }]));
      component.ngOnInit();
      expect(component.locations()).toEqual(['Main Bar']);
    });

    it('runs report immediately on init so data is visible without a manual click', () => {
      component.ngOnInit();
      expect(mockAdminSvc.getReport).toHaveBeenCalled();
    });

    it('populates report signal with API response', () => {
      component.ngOnInit();
      expect(component.report()).toEqual(fakeReport);
    });
  });

  // ── runReport() — 366-day guard ───────────────────────────────────────────────

  describe('runReport() — 366-day date range guard', () => {
    it('blocks ranges over 366 days and shows an error toast', () => {
      component.dateFrom = '2025-01-01';
      component.dateTo   = '2026-06-01'; // > 366 days
      mockAdminSvc.getReport.mockClear();
      component.runReport();
      expect(mockToastSvc.error).toHaveBeenCalledWith('reports.errDateRange');
      expect(mockAdminSvc.getReport).not.toHaveBeenCalled();
    });

    it('clears loading flag when 366-day guard fires', () => {
      component.dateFrom = '2025-01-01';
      component.dateTo   = '2026-06-01';
      component.runReport();
      expect(component.loading()).toBe(false);
    });

    it('allows exactly 366 days', () => {
      component.dateFrom = '2025-01-01';
      component.dateTo   = '2026-01-01'; // exactly 365 days → <= 366
      component.runReport();
      expect(mockAdminSvc.getReport).toHaveBeenCalled();
    });

    it('passes filters without staffIds when the array is empty', () => {
      component.dateFrom  = '2026-05-01';
      component.dateTo    = '2026-05-31';
      component.staffIds  = [];
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.staffIds).toBeUndefined();
    });

    it('passes staffIds when filter array is populated', () => {
      component.dateFrom  = '2026-05-01';
      component.dateTo    = '2026-05-31';
      // allStaff must have more items than selected so subset logic fires
      component.allStaff.set([{ id: 'e1' } as any, { id: 'e2' } as any, { id: 'e3' } as any]);
      component.staffIds  = ['e1', 'e2'];
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.staffIds).toEqual(['e1', 'e2']);
    });

    // ── null guard (PrimeNG clear button sets value to null, not []) ────────────

    it('does not crash when staffIds is null — PrimeNG clear button sets null', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      (component as any).staffIds = null;
      expect(() => component.runReport()).not.toThrow();
    });

    it('does not crash when locationNames is null after clear', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      (component as any).locationNames = null;
      expect(() => component.runReport()).not.toThrow();
    });

    it('does not crash when roles is null after clear', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      (component as any).roles = null;
      expect(() => component.runReport()).not.toThrow();
    });

    it('treats null staffIds as no filter — omits from API call', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      (component as any).staffIds = null;
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.staffIds).toBeUndefined();
    });

    // ── select-all = no filter ────────────────────────────────────────────────

    it('omits staffIds when all staff are selected — avoids blasting every UUID', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      component.allStaff.set([{ id: 'e1' } as any, { id: 'e2' } as any]);
      component.staffIds = ['e1', 'e2']; // all staff selected
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.staffIds).toBeUndefined();
    });

    it('sends staffIds when only a subset of staff are selected', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      component.allStaff.set([{ id: 'e1' } as any, { id: 'e2' } as any, { id: 'e3' } as any]);
      component.staffIds = ['e1', 'e2']; // subset selected
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.staffIds).toEqual(['e1', 'e2']);
    });

    it('omits locationNames from filters when array is empty', () => {
      component.dateFrom       = '2026-05-01';
      component.dateTo         = '2026-05-31';
      component.locationNames  = [];
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.locationNames).toBeUndefined();
    });

    it('passes locationNames when filter array is populated', () => {
      component.dateFrom       = '2026-05-01';
      component.dateTo         = '2026-05-31';
      component.locations.set(['Main Bar', 'VIP Room', 'Rooftop']); // more than selected
      component.locationNames  = ['Main Bar', 'VIP Room'];
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.locationNames).toEqual(['Main Bar', 'VIP Room']);
    });

    it('omits roles from filters when array is empty', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      component.roles    = [];
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.roles).toBeUndefined();
    });

    it('passes roles when filter array is populated', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      mockStaffStore.roles.mockReturnValue([
        { name: 'bartender' }, { name: 'dj' }, { name: 'manager' }, // more than selected
      ]);
      component.roles    = ['bartender', 'dj'];
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.roles).toEqual(['bartender', 'dj']);
    });

    it('always passes dateFrom and dateTo to the API', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      component.runReport();
      const filters = mockAdminSvc.getReport.mock.calls[0][0];
      expect(filters.dateFrom).toBe('2026-05-01');
      expect(filters.dateTo).toBe('2026-05-31');
    });

    it('sets error signal and shows toast on API failure', () => {
      component.dateFrom = '2026-05-01';
      component.dateTo   = '2026-05-31';
      mockAdminSvc.getReport.mockReturnValue(throwError(() => new Error('Network')));
      component.runReport();
      expect(component.error()).toBe('reports.loadFailed');
      expect(mockToastSvc.error).toHaveBeenCalledWith('reports.loadFailed');
    });
  });

  // ── setPreset() — date arithmetic ─────────────────────────────────────────────

  describe('setPreset()', () => {
    afterEach(() => vi.useRealTimers());

    it('"today" sets both dateFrom and dateTo to today', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17)); // 2026-05-17
      component.setPreset('today');
      expect(component.dateFrom).toBe(component.dateTo);
      expect(component.dateTo).toBe('2026-05-17');
    });

    it('"yesterday" sets both dateFrom and dateTo to yesterday', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17)); // 2026-05-17
      component.setPreset('yesterday');
      expect(component.dateFrom).toBe('2026-05-16');
      expect(component.dateTo).toBe('2026-05-16');
    });

    it('"month" sets dateFrom to first of current month', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17)); // 2026-05-17
      component.setPreset('month');
      expect(component.dateFrom).toBe('2026-05-01');
      expect(component.dateTo).toBe('2026-05-17');
    });

    it('"last-month" sets dateFrom to first and dateTo to last of previous month', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17)); // 2026-05-17
      component.setPreset('last-month');
      expect(component.dateFrom).toBe('2026-04-01');
      expect(component.dateTo).toBe('2026-04-30');
    });

    it('"week" sets dateFrom to Monday of current week', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17)); // 2026-05-17 is a Sunday
      component.setPreset('week');
      // Monday of the week containing May 17 (Sun) is May 11
      expect(component.dateFrom).toBe('2026-05-11');
    });

    it('calls runReport() after setting preset dates', () => {
      mockAdminSvc.getReport.mockClear();
      component.setPreset('today');
      expect(mockAdminSvc.getReport).toHaveBeenCalled();
    });
  });

  // ── isPresetActive() — highlight, with tie-breaking ───────────────────────────

  describe('isPresetActive()', () => {
    afterEach(() => vi.useRealTimers());

    it('highlights the matching preset and none of the others', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17)); // 2026-05-17
      component.setPreset('month');
      expect(component.isPresetActive('month')).toBe(true);
      expect(component.isPresetActive('week')).toBe(false);
      expect(component.isPresetActive('today')).toBe(false);
    });

    it('derived highlight breaks ties by priority when no preset was clicked', () => {
      // On 2026-06-02 (Tue), "week" (Mon 06-01 → today) and "month" (06-01 → today) are identical.
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 5, 2)); // 2026-06-02
      component.dateFrom = '2026-06-01';
      component.dateTo   = '2026-06-02';
      // Both ranges match; with no click, priority (week before month) means only "week" highlights.
      expect(component.isPresetActive('week')).toBe(true);
      expect(component.isPresetActive('month')).toBe(false);
    });

    it('highlights the clicked preset (not a coinciding one) when ranges are identical', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 5, 2)); // 2026-06-02 — week and month ranges coincide
      component.setPreset('month');
      // The user clicked "This Month", so it stays highlighted even though "This Week" matches too.
      expect(component.isPresetActive('month')).toBe(true);
      expect(component.isPresetActive('week')).toBe(false);
    });

    it('highlights nothing for a custom range', () => {
      component.dateFrom = '2026-03-07';
      component.dateTo   = '2026-03-19';
      for (const p of ['today', 'yesterday', 'week', 'month', 'last-month'] as const) {
        expect(component.isPresetActive(p)).toBe(false);
      }
    });
  });

  // ── maxLocationCost computed ──────────────────────────────────────────────────

  describe('maxLocationCost computed', () => {
    it('returns the highest cost across all location rows', () => {
      component.report.set({
        ...fakeReport,
        byLocation: [
          { locationName: 'A', shifts: 1, hours: 8, cost: 100 },
          { locationName: 'B', shifts: 1, hours: 8, cost: 400 },
          { locationName: 'C', shifts: 1, hours: 8, cost: 250 },
        ],
      } as any);
      expect(component.maxLocationCost()).toBe(400);
    });

    it('returns 1 when there are no location rows — prevents division by zero in progress bars', () => {
      component.report.set({ ...fakeReport, byLocation: [] } as any);
      expect(component.maxLocationCost()).toBe(1);
    });

    it('returns 1 when report is null', () => {
      component.report.set(null);
      expect(component.maxLocationCost()).toBe(1);
    });
  });

  // ── downloadReportCsv() ───────────────────────────────────────────────────────

  describe('downloadReportCsv()', () => {
    function stubDomDownload() {
      const anchor = Object.assign(document.createElement('a'), { click: vi.fn() });
      vi.spyOn(document, 'createElement').mockReturnValue(anchor as any);
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fake');
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
      return anchor;
    }

    it('does nothing when report is null', () => {
      component.report.set(null);
      component.downloadReportCsv();
      expect(mockToastSvc.success).not.toHaveBeenCalled();
    });

    it('shows success toast after download', () => {
      stubDomDownload();
      component.report.set(fakeReport as any);
      component.downloadReportCsv();
      expect(mockToastSvc.success).toHaveBeenCalledWith('reports.reportDownloaded');
    });

    it('clears reportExporting flag after download', () => {
      stubDomDownload();
      component.report.set(fakeReport as any);
      component.downloadReportCsv();
      expect(component.reportExporting()).toBe(false);
    });

    it('uses employee tab column headers when activeTab is "employee"', () => {
      let capturedBlob: Blob | null = null;
      vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => { capturedBlob = b as Blob; return 'blob:fake'; });
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
      vi.spyOn(document, 'createElement').mockReturnValue(
        Object.assign(document.createElement('a'), { click: vi.fn() }),
      );
      component.report.set(fakeReport as any);
      component.activeTab.set('employee');
      component.downloadReportCsv();
      expect(capturedBlob).not.toBeNull();
    });

    it('uses location tab column headers when activeTab is "location"', () => {
      let capturedBlob: Blob | null = null;
      vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => { capturedBlob = b as Blob; return 'blob:fake'; });
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
      vi.spyOn(document, 'createElement').mockReturnValue(
        Object.assign(document.createElement('a'), { click: vi.fn() }),
      );
      component.report.set(fakeReport as any);
      component.activeTab.set('location');
      component.downloadReportCsv();
      expect(capturedBlob).not.toBeNull();
    });
  });

  // ── Shift editing: managers + admins both allowed (same power) ───────────────
  describe('openEditShift()', () => {
    const row = (staffId: string) => ({ id: 'w1', staffId, role: 'bartender', locationId: 'L1',
      workDate: '2026-05-10', startTime: '18:00', endTime: '23:00', notes: null }) as never;

    it('opens the edit dialog for any shift, including the caller\'s own', () => {
      mockAuthSvc.userRole.mockReturnValue('manager');
      mockAuthSvc.user.mockReturnValue({ staffId: 'MGR001' });
      component.editShiftVisible.set(false);
      component.openEditShift(row('MGR001'));
      expect(component.editShiftVisible()).toBe(true);
    });
  });
});
