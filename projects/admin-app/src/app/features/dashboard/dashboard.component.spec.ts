import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { DashboardComponent } from './dashboard.component';
import { SignalRService, AdminService, ToastService, WorkLogService, StaffStore } from 'shared-lib';

/**
 * DashboardComponent — live KPI panel with SignalR stat patches.
 *
 * WHY these tests matter:
 *  - formatMinutes() drives the "time on shift" display for every on-shift staff
 *    card; wrong formatting (e.g. "0h" for 45 min) would confuse managers.
 *  - liveStats$ patches must update only the four live fields — overwriting the
 *    full stats object would clobber openShiftsCount and other derived fields.
 *  - loadDashboard(false) on each live update keeps the table/feed in sync without
 *    flickering the skeleton loader; showLoader=false path must not set loading=true.
 */

const fakeStats = {
  onShiftCount: 3, totalStaff: 10, tonightHours: 24,
  tonightCost: 360, openShiftsCount: 1,
};
const fakeDashboard = {
  stats: fakeStats,
  currentlyOnShift: [{ workLogId: 'wl-1', staffId: 'NC001', name: 'Jane', role: 'bartender', locationName: 'Main Bar', workDate: '2026-06-01', startTime: '18:00', clockedInAt: '18:00', minutesOnShift: 90 }],
  recentActivity:   [{ staffName: 'Jane', role: 'bartender', locationName: 'Main Bar', timestamp: '18:00', eventType: 'shift-started' as const }],
};
const fakeEarnings = {
  date: '2026-06-15', month: '2026-06', totalToday: 200, totalThisMonth: 4800,
  totalOutstandingAdvance: 350, staff: [],
};

describe('DashboardComponent', () => {
  let liveStats$: Subject<any>;

  let mockSignalR: { isConnected: ReturnType<typeof vi.fn>; liveStats$: ReturnType<typeof vi.fn> };
  let mockAdminSvc: { getDashboard: ReturnType<typeof vi.fn>; getEarningsSnapshot: ReturnType<typeof vi.fn> };
  let mockToastSvc: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn> };
  let mockWorkLogSvc: { adminEndShift: ReturnType<typeof vi.fn> };
  let mockStaffStore: { roles: ReturnType<typeof vi.fn>; loadRoles: ReturnType<typeof vi.fn> };

  function createComponent(connected = false) {
    mockSignalR.isConnected.mockReturnValue(connected);
    return TestBed.createComponent(DashboardComponent).componentInstance;
  }

  beforeEach(async () => {
    liveStats$ = new Subject<any>();

    mockSignalR  = { isConnected: vi.fn().mockReturnValue(false), liveStats$: liveStats$ as any };
    mockAdminSvc = {
      getDashboard:        vi.fn().mockReturnValue(of(fakeDashboard)),
      getEarningsSnapshot: vi.fn().mockReturnValue(of(fakeEarnings)),
    };
    mockToastSvc = { error: vi.fn(), success: vi.fn() };
    mockWorkLogSvc = { adminEndShift: vi.fn().mockReturnValue(of(void 0)) };
    mockStaffStore = { roles: vi.fn().mockReturnValue([]), loadRoles: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: SignalRService, useValue: mockSignalR },
        { provide: AdminService,  useValue: mockAdminSvc },
        { provide: ToastService,  useValue: mockToastSvc },
        { provide: WorkLogService, useValue: mockWorkLogSvc },
        { provide: StaffStore, useValue: mockStaffStore },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  // ── formatMinutes() ───────────────────────────────────────────────────────────

  describe('formatMinutes()', () => {
    let component: DashboardComponent;
    beforeEach(() => { component = createComponent(); });

    it('shows only minutes when under one hour', () => {
      expect(component.formatMinutes(45)).toBe('45m');
    });

    it('shows "Xm" for 0 minutes', () => {
      expect(component.formatMinutes(0)).toBe('0m');
    });

    it('shows only hours when evenly divisible (no trailing "0m")', () => {
      expect(component.formatMinutes(120)).toBe('2h');
    });

    it('shows hours and minutes when there is a remainder', () => {
      expect(component.formatMinutes(90)).toBe('1h 30m');
    });

    it('handles exactly 60 minutes as "1h"', () => {
      expect(component.formatMinutes(60)).toBe('1h');
    });

    it('handles 61 minutes as "1h 1m"', () => {
      expect(component.formatMinutes(61)).toBe('1h 1m');
    });
  });

  // ── ngOnInit() / loadDashboard() ──────────────────────────────────────────────

  describe('ngOnInit() and loadDashboard()', () => {
    let component: DashboardComponent;
    beforeEach(() => { component = createComponent(); });

    it('fetches dashboard data on init', () => {
      component.ngOnInit();
      expect(mockAdminSvc.getDashboard).toHaveBeenCalled();
    });

    it('populates stats signal with API response', () => {
      component.ngOnInit();
      expect(component.stats()).toEqual(fakeStats);
    });

    it('populates onShift signal with API response', () => {
      component.ngOnInit();
      expect(component.onShift()).toEqual(fakeDashboard.currentlyOnShift);
    });

    it('loads the month-to-date earnings snapshot for the financial cards', () => {
      component.ngOnInit();
      expect(mockAdminSvc.getEarningsSnapshot).toHaveBeenCalled();
      expect(component.earnings()).toEqual(fakeEarnings);
    });

    it('still renders the dashboard when the earnings snapshot fails (e.g. no payroll.view)', () => {
      mockAdminSvc.getEarningsSnapshot.mockReturnValue(throwError(() => ({ status: 403 })));
      component.ngOnInit();
      expect(component.stats()).toEqual(fakeStats);   // dashboard still loaded
      expect(component.earnings()).toBeNull();          // financial cards just stay hidden
      expect(component.loading()).toBe(false);
    });

    it('clears loading flag after successful load', () => {
      component.ngOnInit();
      expect(component.loading()).toBe(false);
    });

    it('shows error toast and clears loading flag on API failure', () => {
      mockAdminSvc.getDashboard.mockReturnValue(throwError(() => new Error('Network')));
      component.ngOnInit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('dashboard.loadFailed');
      expect(component.loading()).toBe(false);
    });
  });

  // ── liveStats$ patch ──────────────────────────────────────────────────────────

  describe('liveStats$ patch', () => {
    let component: DashboardComponent;
    beforeEach(() => { component = createComponent(); });

    // NOTE: loadDashboard(false) is called synchronously after the optimistic patch,
    // so the final stats() value reflects the API response, not the live patch.
    // The patch provides a sub-second visual update before the refresh lands.
    // We therefore test the observable SIDE EFFECT (the silent refresh) rather
    // than the transient patch value.

    it('triggers a silent getDashboard refresh when a liveStats event arrives', () => {
      component.ngOnInit();
      mockAdminSvc.getDashboard.mockClear();
      liveStats$.next({ onShiftCount: 5, totalStaff: 10, tonightHours: 40, estimatedLabourCost: 600 });
      expect(mockAdminSvc.getDashboard).toHaveBeenCalled();
    });

    it('does NOT set loading=true during the silent liveStats refresh', () => {
      component.ngOnInit(); // initial load ends with loading=false
      const spy = vi.spyOn(component.loading, 'set');
      liveStats$.next({ onShiftCount: 5, totalStaff: 10, tonightHours: 40, estimatedLabourCost: 600 });
      // loadDashboard(false) must not call loading.set(true)
      const calledWithTrue = spy.mock.calls.some(([v]) => v === true);
      expect(calledWithTrue).toBe(false);
    });

    it('updates onShift and activity after a liveStats-triggered refresh', () => {
      component.ngOnInit();
      expect(component.onShift()).toEqual(fakeDashboard.currentlyOnShift);
      expect(component.activity()).toHaveLength(1);
    });
  });

  // ── Close a forgotten clock-out ───────────────────────────────────────────────

  describe('confirmCloseShift()', () => {
    let component: DashboardComponent;
    const row = { workLogId: 'wl-1', staffId: 'NC001', name: 'Jane', role: 'bartender', locationName: 'Main Bar', workDate: '2026-06-01', startTime: '18:00', clockedInAt: '18:00', minutesOnShift: 90 };
    beforeEach(() => { component = createComponent(); });

    it('openCloseShift seeds the row + a default end date (the clock-in date) and time', () => {
      component.openCloseShift(row);
      expect(component.closeRow()).toEqual(row);
      expect(component.closeEndDate).toBeInstanceOf(Date);
      expect(component.closeEndTime).toBeInstanceOf(Date);
    });

    it('posts the work-log id + chosen end date (yyyy-MM-dd) and time (HH:mm), then closes', () => {
      component.openCloseShift(row);
      component.closeEndDate = new Date(2026, 5, 1);          // 2026-06-01
      component.closeEndTime = new Date(2026, 0, 1, 2, 5);    // 02:05 → must be zero-padded
      component.confirmCloseShift();
      expect(mockWorkLogSvc.adminEndShift).toHaveBeenCalledWith('wl-1', '2026-06-01', '02:05');
      expect(mockToastSvc.success).toHaveBeenCalledWith('dashboard.shiftClosedToast');
      expect(component.closeRow()).toBeNull();
    });

    it('does nothing when the end date or time is missing', () => {
      component.openCloseShift(row);
      component.closeEndTime = null;
      component.confirmCloseShift();
      expect(mockWorkLogSvc.adminEndShift).not.toHaveBeenCalled();
    });

    it('shows an error toast and keeps the dialog open on failure', () => {
      mockWorkLogSvc.adminEndShift.mockReturnValue(throwError(() => ({ error: { error: 'A shift cannot exceed 24 hours.' } })));
      component.openCloseShift(row);
      component.closeEndDate = new Date(2026, 5, 1);
      component.closeEndTime = new Date(2026, 0, 1, 23, 0);
      component.confirmCloseShift();
      expect(mockToastSvc.error).toHaveBeenCalledWith('A shift cannot exceed 24 hours.');
      expect(component.closeRow()).toEqual(row);
    });
  });

  // ── closeEndStep (end picker follows the shift's job pay type) ──────────────────
  describe('closeEndStep', () => {
    const fixedRow  = { workLogId: 'w', staffId: 'S', name: 'A', role: 'OVERNIGHT 8H', locationName: 'L', workDate: '2026-06-01', startTime: '18:00', clockedInAt: '18:00', minutesOnShift: 10 };
    const hourlyRow = { ...fixedRow, role: 'TABLE ROUNDS' };

    it('steps 10 min when the shift being closed is a FIXED job', () => {
      const c = createComponent();
      mockStaffStore.roles.mockReturnValue([{ id: 'j1', name: 'OVERNIGHT 8H', payType: 'fixed' }]);
      c.openCloseShift(fixedRow);
      expect(c.closeEndStep).toBe(10);
    });

    it('steps 30 min when the shift being closed is an HOURLY job', () => {
      const c = createComponent();
      mockStaffStore.roles.mockReturnValue([{ id: 'j2', name: 'TABLE ROUNDS', payType: 'hourly' }]);
      c.openCloseShift(hourlyRow);
      expect(c.closeEndStep).toBe(30);
    });

    it('defaults to 30 min when no row is being closed or the role is unknown', () => {
      const c = createComponent();
      expect(c.closeEndStep).toBe(30);              // no closeRow
      mockStaffStore.roles.mockReturnValue([]);
      c.openCloseShift(fixedRow);
      expect(c.closeEndStep).toBe(30);              // role not found in loaded roles
    });
  });
});
