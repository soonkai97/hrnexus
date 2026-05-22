import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ReportsComponent } from './reports.component';
import {
  AdminService, StaffStore, StaffManagementService, ToastService,
  StaffService, FeatureFlagService, StaffLedgerPayoutDto, StaffOutstandingDto,
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
 *  - loadLedgerData() fetches summary + outstanding in parallel; errors on either leg
 *    must not crash the page — users only see a toast.
 *  - downloadLedgerCsv() must produce correct column headers and skip when empty.
 */

// Minimal ReportResponse shape — real API may have more fields
const fakeReport = {
  summary:    { totalShifts: 10, totalHours: 80, totalCost: 1200 },
  byStaff:    [{ staffId: 'NC001', name: 'Jane Doe', role: 'bartender', shifts: 5, hours: 40, avgHoursPerShift: 8, cost: 600 }],
  byLocation: [{ locationName: 'Main Bar', shifts: 8, hours: 64, cost: 960 }],
  byRole:     [{ role: 'bartender', staffCount: 1, shifts: 5, hours: 40, cost: 600 }],
  byDate:     [{ date: '2026-05-10', shifts: 2, hours: 16, cost: 240 }],
  shifts:     [{
    staffId: 'NC001', staffName: 'Jane Doe', role: 'bartender',
    locationName: 'Main Bar', clockedIn: '18:00', clockedOut: '23:00',
    hours: 5, cost: 75, status: 'completed', isOvertime: false, otThreshold: 0,
  }],
};

const fakePayout: StaffLedgerPayoutDto[] = [
  {
    staffId: 'uuid-1', staffCode: 'NC001', staffName: 'Alice',
    venueEarnings: 500, privateNet: 200, netPayout: 700,
    entryCount: 10, pendingCount: 2, notCollectedCount: 1,
  },
  {
    staffId: 'uuid-2', staffCode: 'NC002', staffName: 'Bob',
    venueEarnings: 300, privateNet: 0, netPayout: 300,
    entryCount: 6, pendingCount: 0, notCollectedCount: 0,
  },
];

const fakeOutstanding: StaffOutstandingDto[] = [
  {
    entryId: 'e1', staffId: 'uuid-1', staffCode: 'NC001', staffName: 'Alice',
    entryDate: '2026-05-01', workType: 'VENUECLOCK',
    venueEarnings: 120, privateNet: 0, settlement: 'PENDING',
  },
];

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
  let mockStaffLedgerSvc: { summary: ReturnType<typeof vi.fn>; outstanding: ReturnType<typeof vi.fn>; bulkSettle: ReturnType<typeof vi.fn> };
  let mockFeatureSvc:     { isEnabled: ReturnType<typeof vi.fn> };

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
    mockStaffLedgerSvc = {
      summary:     vi.fn().mockReturnValue(of(fakePayout)),
      outstanding: vi.fn().mockReturnValue(of(fakeOutstanding)),
      bulkSettle:  vi.fn().mockReturnValue(of({ updated: 1 })),
    };
    mockFeatureSvc = { isEnabled: vi.fn().mockReturnValue(false) };

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
        { provide: StaffService,          useValue: mockStaffLedgerSvc },
        { provide: FeatureFlagService,    useValue: mockFeatureSvc },
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
      expect(mockToastSvc.error).toHaveBeenCalledWith(
        'Date range cannot exceed 366 days. Please narrow your filter.',
      );
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
      expect(component.error()).toBe('Failed to load report.');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to load report.');
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
      expect(mockToastSvc.success).toHaveBeenCalledWith('Report downloaded.');
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

  // ── loadLedgerData() ─────────────────────────────────────────────────────────

  describe('loadLedgerData()', () => {
    it('calls summary() with current ledgerYear and ledgerMonth', () => {
      component.ledgerYear.set(2026);
      component.ledgerMonth.set(5);
      component.loadLedgerData();
      expect(mockStaffLedgerSvc.summary).toHaveBeenCalledWith(2026, 5);
    });

    it('populates ledgerPayout signal with summary results', () => {
      component.loadLedgerData();
      expect(component.ledgerPayout()).toEqual(fakePayout);
    });

    it('populates ledgerOutstanding signal with outstanding results', () => {
      component.loadLedgerData();
      expect(component.ledgerOutstanding()).toEqual(fakeOutstanding);
    });

    it('clears ledgerLoading after successful summary load', () => {
      component.loadLedgerData();
      expect(component.ledgerLoading()).toBe(false);
    });

    it('shows toast and clears loading on summary API error', () => {
      mockStaffLedgerSvc.summary.mockReturnValue(throwError(() => new Error('Network')));
      component.loadLedgerData();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to load payout data.');
      expect(component.ledgerLoading()).toBe(false);
    });

    it('does not crash when outstanding API errors — outstanding is optional', () => {
      mockStaffLedgerSvc.outstanding.mockReturnValue(throwError(() => new Error('Net')));
      expect(() => component.loadLedgerData()).not.toThrow();
    });
  });

  // ── Ledger computed totals ────────────────────────────────────────────────────

  describe('ledger computed totals', () => {
    beforeEach(() => component.ledgerPayout.set(fakePayout));

    it('ledgerTotalVenue sums all venueEarnings rows', () => {
      expect(component.ledgerTotalVenue()).toBe(800); // 500 + 300
    });

    it('ledgerTotalPrivate sums all privateNet rows', () => {
      expect(component.ledgerTotalPrivate()).toBe(200); // 200 + 0
    });

    it('ledgerTotalPayout sums all netPayout rows', () => {
      expect(component.ledgerTotalPayout()).toBe(1000); // 700 + 300
    });

    it('totals are 0 when ledgerPayout is empty', () => {
      component.ledgerPayout.set([]);
      expect(component.ledgerTotalVenue()).toBe(0);
      expect(component.ledgerTotalPrivate()).toBe(0);
      expect(component.ledgerTotalPayout()).toBe(0);
    });
  });

  // ── downloadLedgerCsv() ───────────────────────────────────────────────────────

  describe('downloadLedgerCsv()', () => {
    function stubDomDownload() {
      const anchor = Object.assign(document.createElement('a'), { click: vi.fn() });
      vi.spyOn(document, 'createElement').mockReturnValue(anchor as any);
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fake');
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
      return anchor;
    }

    it('does nothing when ledgerPayout is empty', () => {
      component.ledgerPayout.set([]);
      component.downloadLedgerCsv();
      expect(mockToastSvc.success).not.toHaveBeenCalled();
    });

    it('creates a CSV blob and triggers download when payout rows exist', () => {
      stubDomDownload();
      component.ledgerPayout.set(fakePayout);
      let capturedBlob: Blob | null = null;
      vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => { capturedBlob = b as Blob; return 'blob:fake'; });
      component.downloadLedgerCsv();
      expect(capturedBlob).not.toBeNull();
    });

    it('shows success toast after download', () => {
      stubDomDownload();
      component.ledgerPayout.set(fakePayout);
      component.downloadLedgerCsv();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Payout report downloaded.');
    });

    it('clears ledgerExporting flag after download', () => {
      stubDomDownload();
      component.ledgerPayout.set(fakePayout);
      component.downloadLedgerCsv();
      expect(component.ledgerExporting()).toBe(false);
    });
  });

  // ── bulkSettle() ─────────────────────────────────────────────────────────────

  describe('bulkSettle()', () => {
    beforeEach(() => {
      component.ledgerOutstanding.set(fakeOutstanding);
      component.outstandingSelection = [...fakeOutstanding]; // select all
    });

    it('does nothing when no rows are selected', () => {
      component.outstandingSelection = [];
      component.bulkSettle();
      expect(mockStaffLedgerSvc.bulkSettle).not.toHaveBeenCalled();
    });

    it('calls bulkSettle with the selected entryIds and chosen settlement', () => {
      component.settleWith.set('CASH');
      component.bulkSettle();
      expect(mockStaffLedgerSvc.bulkSettle).toHaveBeenCalledWith(
        fakeOutstanding.map(e => e.entryId),
        'CASH',
      );
    });

    it('shows success toast with entry count after settle', () => {
      mockStaffLedgerSvc.bulkSettle.mockReturnValue(of({ updated: 2 }));
      component.outstandingSelection = [...fakeOutstanding, ...fakeOutstanding]; // 2
      component.bulkSettle();
      expect(mockToastSvc.success).toHaveBeenCalledWith('2 entries settled.');
    });

    it('shows singular "entry" for a single settle', () => {
      mockStaffLedgerSvc.bulkSettle.mockReturnValue(of({ updated: 1 }));
      component.bulkSettle();
      expect(mockToastSvc.success).toHaveBeenCalledWith('1 entry settled.');
    });

    it('clears selection after successful settle', () => {
      component.bulkSettle();
      expect(component.outstandingSelection).toHaveLength(0);
    });

    it('reloads payout data after settle so counts update', () => {
      mockStaffLedgerSvc.summary.mockClear();
      component.bulkSettle();
      expect(mockStaffLedgerSvc.summary).toHaveBeenCalled();
    });

    it('clears settling flag after success', () => {
      component.bulkSettle();
      expect(component.settling()).toBe(false);
    });

    it('shows error toast and clears settling flag on API failure', () => {
      mockStaffLedgerSvc.bulkSettle.mockReturnValue(throwError(() => new Error('Network')));
      component.bulkSettle();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to settle entries.');
      expect(component.settling()).toBe(false);
    });
  });

  // ── isStaffLedgerEnabled computed ─────────────────────────────────────────────

  describe('isStaffLedgerEnabled computed', () => {
    it('returns false when FeatureFlagService.isEnabled returns false', () => {
      mockFeatureSvc.isEnabled.mockReturnValue(false);
      expect(component.isStaffLedgerEnabled()).toBe(false);
    });

    it('returns true when FeatureFlagService.isEnabled returns true', () => {
      mockFeatureSvc.isEnabled.mockReturnValue(true);
      expect(component.isStaffLedgerEnabled()).toBe(true);
    });
  });
});
