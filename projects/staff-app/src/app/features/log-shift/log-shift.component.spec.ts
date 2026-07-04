import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { LogShiftComponent } from './log-shift.component';
import { WorkLogService, WorkLogDto, LocationStore, ToastService, ConfirmService, AuthService } from 'shared-lib';

/**
 * LogShiftComponent — live clock-in/out and manual shift entry.
 *
 * WHY these tests matter:
 *  - startShift() must reject missing role/location before hitting the API —
 *    a shift logged without a role cannot be matched to a pay rate.
 *  - confirmEnd() must clear the activeLog signal so the UI does not show a
 *    ghost "active shift" after the staff member has already clocked out.
 *  - submitManual() must reject incomplete forms — a manual entry missing a
 *    date or time would produce an invalid work record.
 *  - deleteLog() must require explicit confirmation before deleting a shift
 *    record since deletion is permanent and affects payroll calculations.
 */

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeLog(overrides: Partial<WorkLogDto> = {}): WorkLogDto {
  return {
    id:            'log-1',
    roleName:      'bartender',
    locationId:    'loc-1',
    locationName:  'Main Bar',
    workDate:      '2026-05-17',
    startTime:     '18:00',
    endDate:       null,
    endTime:       null,
    hours:         0,
    cost:          0,
    isActive:      true,
    isManualEntry: false,
    isEdited:      false,
    notes:         null,
    ...overrides,
  };
}

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('LogShiftComponent', () => {
  let component: LogShiftComponent;

  let mockWorkLogSvc: {
    getActive:    ReturnType<typeof vi.fn>;
    getMy:        ReturnType<typeof vi.fn>;
    start:        ReturnType<typeof vi.fn>;
    end:          ReturnType<typeof vi.fn>;
    createManual: ReturnType<typeof vi.fn>;
    delete:       ReturnType<typeof vi.fn>;
  };
  let mockLocationStore: { load: ReturnType<typeof vi.fn>; locations: () => any[]; loadMine: ReturnType<typeof vi.fn>; myLocations: () => any[]; myLoaded: () => boolean };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };
  let mockAuth:       { user: () => any };

  beforeEach(async () => {
    mockWorkLogSvc = {
      getActive:    vi.fn().mockReturnValue(of(null)),
      getMy:        vi.fn().mockReturnValue(of({ items: [], total: 0 })),
      start:        vi.fn().mockReturnValue(of({ id: 'log-new' })),
      end:          vi.fn().mockReturnValue(of(undefined)),
      createManual: vi.fn().mockReturnValue(of({ id: 'log-manual' })),
      delete:       vi.fn().mockReturnValue(of(undefined)),
    };
    mockLocationStore = {
      load:        vi.fn(),
      locations:   () => [{ id: 'loc-1', name: 'Main Bar' }],
      loadMine:    vi.fn(),
      myLocations: () => [{ id: 'loc-1', name: 'Main Bar' }],
      myLoaded:    () => true,
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };
    mockAuth       = { user: () => ({ id: 'u1', staffId: 'NC001', fullName: 'Test User', email: 't@test.com',
      roles: ['EMPLOYEE', 'BARTENDER'],
      jobs: [{ id: 'job-emp', name: 'EMPLOYEE' }, { id: 'job-bar', name: 'BARTENDER' }] }) };

    await TestBed.configureTestingModule({
      imports: [LogShiftComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: WorkLogService,   useValue: mockWorkLogSvc },
        { provide: LocationStore,    useValue: mockLocationStore },
        { provide: ToastService,     useValue: mockToastSvc },
        { provide: ConfirmService,   useValue: mockConfirmSvc },
        { provide: AuthService,      useValue: mockAuth },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LogShiftComponent);
    component = fixture.componentInstance;
    // Do NOT call detectChanges() — avoids PrimeNG template initialisation errors.
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads the assigned-location list so role and location selects are populated', () => {
      component.ngOnInit();
      expect(mockLocationStore.loadMine).toHaveBeenCalled();
    });

    it('fetches the active shift so the "clock out" panel shows if already clocked in', () => {
      component.ngOnInit();
      expect(mockWorkLogSvc.getActive).toHaveBeenCalled();
    });

    it('populates activeLog when an active shift exists', () => {
      const active = makeLog({ isActive: true });
      mockWorkLogSvc.getActive.mockReturnValue(of(active));
      component.ngOnInit();
      expect(component.activeLog()).toEqual(active);
    });

    it('fetches shift history for the history tab', () => {
      component.ngOnInit();
      expect(mockWorkLogSvc.getMy).toHaveBeenCalled();
    });

    it('populates history signal with non-active shift records', () => {
      const completed = makeLog({ isActive: false });
      mockWorkLogSvc.getMy.mockReturnValue(of({ items: [completed], total: 1 }));
      component.ngOnInit();
      expect(component.history()).toEqual([completed]);
    });

    it('excludes active shifts from history list', () => {
      const active    = makeLog({ id: 'a', isActive: true });
      const completed = makeLog({ id: 'b', isActive: false });
      mockWorkLogSvc.getMy.mockReturnValue(of({ items: [active, completed], total: 2 }));
      component.ngOnInit();
      expect(component.history()).toEqual([completed]);
    });

    it('isEdited flag from history items is preserved in signal', () => {
      const editedLog = makeLog({ isActive: false, isEdited: true });
      mockWorkLogSvc.getMy.mockReturnValue(of({ items: [editedLog], total: 1 }));
      component.ngOnInit();
      expect(component.history()[0].isEdited).toBe(true);
    });
  });

  // ── myJobs / myRoleOptions computed ──────────────────────────────────────────

  describe('myJobs / myRoleOptions computed', () => {
    it('filters out SUPERADMIN only — managers should be able to select the MANAGER job when logging a shift', () => {
      mockAuth.user = () => ({ jobs: [
        { id: 'j-emp', name: 'EMPLOYEE' },
        { id: 'j-mgr', name: 'MANAGER' },
        { id: 'j-admin', name: 'SUPERADMIN' },
        { id: 'j-bar', name: 'BARTENDER' },
      ] } as any);
      const fresh = TestBed.createComponent(LogShiftComponent).componentInstance;
      expect(fresh.myJobs().map(j => j.name)).toEqual(['EMPLOYEE', 'MANAGER', 'BARTENDER']);
    });

    it('maps each job to an option with the role id as the value (so non-ASCII names are picked by id)', () => {
      mockAuth.user = () => ({ jobs: [
        { id: 'j-cn', name: '写钟（在外）', nameZh: '写钟（在外）' },
      ] } as any);
      const fresh = TestBed.createComponent(LogShiftComponent).componentInstance;
      expect(fresh.myRoleOptions()).toEqual([{ label: '写钟（在外）', value: 'j-cn' }]);
    });

    it('returns empty array when user has no jobs', () => {
      mockAuth.user = () => ({ jobs: [] } as any);
      const fresh = TestBed.createComponent(LogShiftComponent).componentInstance;
      expect(fresh.myJobs()).toEqual([]);
    });
  });

  // ── startShift() ─────────────────────────────────────────────────────────────

  describe('startShift()', () => {
    it('sets formError and does not call API when roleName is missing', () => {
      component.liveForm.roleId     ='';
      component.liveForm.locationId = 'loc-1';
      component.startShift();
      expect(component.formError()).toBe('logShift.errSelectRoleLocation');
      expect(mockWorkLogSvc.start).not.toHaveBeenCalled();
    });

    it('sets formError and does not call API when locationId is missing', () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = '';
      component.startShift();
      expect(component.formError()).toBe('logShift.errSelectRoleLocation');
      expect(mockWorkLogSvc.start).not.toHaveBeenCalled();
    });

    it('calls start() with correct payload when form is valid', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      component.liveFormStartTime   = new Date(2026, 4, 17, 20, 0); // 20:00
      component.liveForm.notes      = 'Late shift';
      component.startShift();
      await Promise.resolve(); // let the geolocation lookup (microtask) resolve before asserting
      expect(mockWorkLogSvc.start).toHaveBeenCalledWith({
        roleId:     'bartender',
        locationId: 'loc-1',
        startTime:  '20:00',
        notes:      'Late shift',
        latitude:   null,    // jsdom has no geolocation → clock in without coords
        longitude:  null,
      });
    });

    it('converts empty notes to null before sending to the API', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      component.liveForm.notes      = '';
      component.startShift();
      await Promise.resolve();
      expect(mockWorkLogSvc.start).toHaveBeenCalledWith(
        expect.objectContaining({ notes: null }),
      );
    });

    it('shows success toast after shift starts', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      component.startShift();
      await Promise.resolve();
      expect(mockToastSvc.success).toHaveBeenCalledWith('logShift.toast.started');
    });

    it('reloads active shift after success so the clock-out panel appears', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      mockWorkLogSvc.getActive.mockClear();
      component.startShift();
      await Promise.resolve();
      expect(mockWorkLogSvc.getActive).toHaveBeenCalled();
    });

    it('resets liveForm after success', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      component.liveForm.notes      = 'notes';
      component.startShift();
      await Promise.resolve();
      expect(component.liveForm.roleId).toBe('');
      expect(component.liveForm.locationId).toBe('');
      expect(component.liveForm.notes).toBe('');
    });

    it('sets formError from API response on failure', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      mockWorkLogSvc.start.mockReturnValue(
        throwError(() => ({ error: { error: 'You already have an active shift.' } })),
      );
      component.startShift();
      await Promise.resolve();
      expect(component.formError()).toBe('You already have an active shift.');
    });

    it('uses fallback error message when API provides none', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      mockWorkLogSvc.start.mockReturnValue(throwError(() => ({})));
      component.startShift();
      await Promise.resolve();
      expect(component.formError()).toBe('logShift.errStart');
    });

    it('clears submitting flag after error so the button is re-enabled', async () => {
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      mockWorkLogSvc.start.mockReturnValue(throwError(() => ({})));
      component.startShift();
      await Promise.resolve();
      expect(component.submitting()).toBe(false);
    });

    it('includes the device GPS coordinates when geolocation is available', async () => {
      // Proof-of-presence: admin sees where the staff member physically clocked in.
      Object.defineProperty(navigator, 'geolocation', {
        configurable: true,
        value: {
          getCurrentPosition: (success: PositionCallback) =>
            success({ coords: { latitude: 1.23, longitude: 4.56 } } as unknown as GeolocationPosition),
        },
      });
      component.liveForm.roleId     ='bartender';
      component.liveForm.locationId = 'loc-1';
      component.startShift();
      await Promise.resolve();
      expect(mockWorkLogSvc.start).toHaveBeenCalledWith(
        expect.objectContaining({ latitude: 1.23, longitude: 4.56 }),
      );
      delete (navigator as { geolocation?: unknown }).geolocation;
    });
  });

  // ── openEndShift() / cancelEnd() ─────────────────────────────────────────────

  describe('openEndShift() and cancelEnd()', () => {
    it('openEndShift() stores the shift id so confirmEnd() knows which shift to close', () => {
      component.openEndShift('log-42');
      expect(component.endingId()).toBe('log-42');
    });

    it('cancelEnd() clears the endingId so the end-shift form is dismissed', () => {
      component.endingId.set('log-42');
      component.cancelEnd();
      expect(component.endingId()).toBeNull();
    });
  });

  // ── confirmEnd() ─────────────────────────────────────────────────────────────

  describe('confirmEnd()', () => {
    it('does nothing when endingId is null — prevents ghost API calls', () => {
      component.endingId.set(null);
      component.confirmEnd();
      expect(mockWorkLogSvc.end).not.toHaveBeenCalled();
    });

    it('calls end() with the stored shift id', () => {
      component.endingId.set('log-99');
      component.confirmEnd();
      expect(mockWorkLogSvc.end).toHaveBeenCalledWith('log-99', expect.any(String));
    });

    it('clears activeLog after ending the shift — prevents ghost "active shift" UI', () => {
      component.activeLog.set(makeLog({ isActive: true }));
      component.endingId.set('log-1');
      component.confirmEnd();
      expect(component.activeLog()).toBeNull();
    });

    it('clears endingId after success so the end-shift dialog is dismissed', () => {
      component.endingId.set('log-1');
      component.confirmEnd();
      expect(component.endingId()).toBeNull();
    });

    it('shows success toast confirming the shift ended', () => {
      component.endingId.set('log-1');
      component.confirmEnd();
      expect(mockToastSvc.success).toHaveBeenCalledWith('logShift.toast.ended');
    });

    it('refreshes history after ending so the completed shift appears in the log', () => {
      component.endingId.set('log-1');
      mockWorkLogSvc.getMy.mockClear();
      component.confirmEnd();
      expect(mockWorkLogSvc.getMy).toHaveBeenCalled();
    });

    it('shows error toast when ending the shift fails', () => {
      component.endingId.set('log-1');
      mockWorkLogSvc.end.mockReturnValue(
        throwError(() => ({ error: { error: 'Shift already ended.' } })),
      );
      component.confirmEnd();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Shift already ended.');
    });

    it('clears submitting flag after end error', () => {
      component.endingId.set('log-1');
      mockWorkLogSvc.end.mockReturnValue(throwError(() => ({})));
      component.confirmEnd();
      expect(component.submitting()).toBe(false);
    });
  });

  // ── submitManual() ───────────────────────────────────────────────────────────

  describe('submitManual()', () => {
    /** Fill all required fields with valid data. */
    function fillManualForm(): void {
      component.manualForm.roleId     ='bartender';
      component.manualForm.locationId = 'loc-1';
      component.manualForm.workDate   = '2026-05-17';
      component.manualFormStartTime   = new Date(2026, 4, 17, 18, 0); // 18:00
      component.manualFormEndTime     = new Date(2026, 4, 17, 23, 0); // 23:00
    }

    it('sets formError when roleName is missing', () => {
      fillManualForm();
      component.manualForm.roleId = '';
      component.submitManual();
      expect(component.formError()).toBe('logShift.errFillAll');
      expect(mockWorkLogSvc.createManual).not.toHaveBeenCalled();
    });

    it('sets formError when locationId is missing', () => {
      fillManualForm();
      component.manualForm.locationId = '';
      component.submitManual();
      expect(component.formError()).toBe('logShift.errFillAll');
    });

    it('sets formError when workDate is missing', () => {
      fillManualForm();
      component.manualForm.workDate = '';
      component.submitManual();
      expect(component.formError()).toBe('logShift.errFillAll');
    });

    it('sets formError when startTime is missing', () => {
      fillManualForm();
      component.manualFormStartTime = null;
      component.submitManual();
      expect(component.formError()).toBe('logShift.errFillAll');
    });

    it('sets formError when endTime is missing', () => {
      fillManualForm();
      component.manualFormEndTime = null;
      component.submitManual();
      expect(component.formError()).toBe('logShift.errFillAll');
    });

    it('calls createManual() with correct payload when all fields are valid', () => {
      fillManualForm();
      component.manualForm.notes = 'Covered for sick colleague';
      component.submitManual();
      expect(mockWorkLogSvc.createManual).toHaveBeenCalledWith({
        roleId:     'bartender',
        locationId: 'loc-1',
        workDate:   '2026-05-17',
        startTime:  '18:00',
        endTime:    '23:00',
        notes:      'Covered for sick colleague',
      });
    });

    it('converts empty notes to null', () => {
      fillManualForm();
      component.manualForm.notes = '';
      component.submitManual();
      expect(mockWorkLogSvc.createManual).toHaveBeenCalledWith(
        expect.objectContaining({ notes: null }),
      );
    });

    it('sets formError when start and end times are identical — zero-duration shifts are invalid', () => {
      fillManualForm();
      component.manualFormStartTime = new Date(2026, 4, 17, 8, 0);  // 08:00
      component.manualFormEndTime   = new Date(2026, 4, 17, 8, 0);  // 08:00 — same
      component.submitManual();
      expect(component.formError()).toBe('logShift.errSameTime');
      expect(mockWorkLogSvc.createManual).not.toHaveBeenCalled();
    });

    it('supports overnight shifts (startTime > endTime) without blocking submission', () => {
      fillManualForm();
      component.manualFormStartTime = new Date(2026, 4, 17, 22, 0); // 22:00
      component.manualFormEndTime   = new Date(2026, 4, 17, 6, 0);  // 06:00 — next day
      component.submitManual();
      expect(mockWorkLogSvc.createManual).toHaveBeenCalled();
    });

    it('shows success toast after logging a manual shift', () => {
      fillManualForm();
      component.submitManual();
      expect(mockToastSvc.success).toHaveBeenCalledWith('logShift.toast.logged');
    });

    it('resets manualForm after success', () => {
      fillManualForm();
      component.submitManual();
      expect(component.manualForm.roleId).toBe('');
      expect(component.manualForm.locationId).toBe('');
      expect(component.manualFormStartTime).toBeNull();
      expect(component.manualFormEndTime).toBeNull();
    });

    it('reloads history after success so the new entry appears in the list', () => {
      fillManualForm();
      mockWorkLogSvc.getMy.mockClear();
      component.submitManual();
      expect(mockWorkLogSvc.getMy).toHaveBeenCalled();
    });

    it('sets formError from API error on submission failure', () => {
      fillManualForm();
      mockWorkLogSvc.createManual.mockReturnValue(
        throwError(() => ({ error: { error: 'Shift date is in the future.' } })),
      );
      component.submitManual();
      expect(component.formError()).toBe('Shift date is in the future.');
    });

    it('clears submitting flag after error', () => {
      fillManualForm();
      mockWorkLogSvc.createManual.mockReturnValue(throwError(() => ({})));
      component.submitManual();
      expect(component.submitting()).toBe(false);
    });
  });

  describe('endDayOffset()', () => {
    it('returns 0 when the end date matches the work date (same-day shift)', () => {
      expect(component.endDayOffset(makeLog({ workDate: '2026-05-01', endDate: '2026-05-01' }))).toBe(0);
    });
    it('returns 0 when endDate is null (active shift)', () => {
      expect(component.endDayOffset(makeLog({ workDate: '2026-05-01', endDate: null }))).toBe(0);
    });
    it('returns the day span for an overnight (1) and multi-day (>1) shift', () => {
      expect(component.endDayOffset(makeLog({ workDate: '2026-05-01', endDate: '2026-05-02' }))).toBe(1);
      expect(component.endDayOffset(makeLog({ workDate: '2026-05-01', endDate: '2026-05-04' }))).toBe(3);
    });
  });

  // ── overnight date getters ───────────────────────────────────────────────────

  describe('isOvernightManual', () => {
    it('returns false when manualFormStartTime is null', () => {
      component.manualFormStartTime = null;
      component.manualFormEndTime   = new Date(2026, 4, 17, 6, 0);
      expect(component.isOvernightManual).toBe(false);
    });

    it('returns false when manualFormEndTime is null', () => {
      component.manualFormStartTime = new Date(2026, 4, 17, 22, 0);
      component.manualFormEndTime   = null;
      expect(component.isOvernightManual).toBe(false);
    });

    it('returns false for a same-day shift (end > start)', () => {
      component.manualFormStartTime = new Date(2026, 4, 17, 9, 0);  // 09:00
      component.manualFormEndTime   = new Date(2026, 4, 17, 17, 0); // 17:00
      expect(component.isOvernightManual).toBe(false);
    });

    it('returns true when end time is earlier than start (overnight)', () => {
      component.manualFormStartTime = new Date(2026, 4, 17, 22, 0); // 22:00
      component.manualFormEndTime   = new Date(2026, 4, 17, 2, 0);  // 02:00 next day
      expect(component.isOvernightManual).toBe(true);
    });
  });

  describe('manualEndDate', () => {
    it('returns null when the shift is not overnight', () => {
      component.manualForm.workDate = '2026-05-17';
      component.manualFormStartTime = new Date(2026, 4, 17, 9, 0);
      component.manualFormEndTime   = new Date(2026, 4, 17, 17, 0);
      expect(component.manualEndDate).toBeNull();
    });

    it('returns null when workDate is empty even if shift is overnight', () => {
      component.manualForm.workDate = '';
      component.manualFormStartTime = new Date(2026, 4, 17, 22, 0);
      component.manualFormEndTime   = new Date(2026, 4, 17, 2, 0);
      expect(component.manualEndDate).toBeNull();
    });

    it('returns the next calendar date when the shift is overnight — so staff know when they clock out', () => {
      component.manualForm.workDate = '2026-05-17';
      component.manualFormStartTime = new Date(2026, 4, 17, 22, 0); // 22:00
      component.manualFormEndTime   = new Date(2026, 4, 17, 2, 0);  // 02:00
      expect(component.manualEndDate).toBe('2026-05-18');
    });

    it('handles month-end rollover correctly', () => {
      component.manualForm.workDate = '2026-05-31';
      component.manualFormStartTime = new Date(2026, 4, 31, 23, 0);
      component.manualFormEndTime   = new Date(2026, 4, 31, 1, 0);
      expect(component.manualEndDate).toBe('2026-06-01');
    });
  });

  // ── setMode() ────────────────────────────────────────────────────────────────

  describe('setMode()', () => {
    it('switches the active mode', () => {
      component.setMode('manual');
      expect(component.mode()).toBe('manual');
    });

    it('clears formError when switching modes — stale errors from the other mode should not persist', () => {
      component.formError.set('previous error');
      component.setMode('live');
      expect(component.formError()).toBe('');
    });
  });

  // ── check-out picker minute-step by job pay type ─────────────────────────────
  // WHY: fixed packages allow fine 10-min adjustments (minutes don't change their flat pay); hourly
  // jobs snap to the 30-min billing block so a checkout lands on a clean half-hour (it'd be floored
  // anyway). A fresh component per test because myJobs() is a computed over the mocked user.
  describe('check-out step by pay type', () => {
    const jobs = [
      { id: 'j-fixed',  name: 'OVERNIGHT 8H', payType: 'fixed'  },
      { id: 'j-hourly', name: 'TABLE ROUNDS', payType: 'hourly' },
      { id: 'j-legacy', name: 'LEGACY',       payType: null     },
    ];
    let c: LogShiftComponent;

    beforeEach(() => {
      mockAuth.user = () => ({ id: 'u1', staffId: 'NC001', fullName: 'T', email: 't@test.com',
        roles: ['EMPLOYEE'], jobs });
      c = TestBed.createComponent(LogShiftComponent).componentInstance;
    });

    it('manual pickers step 10 min for a FIXED job', () => {
      c.manualForm.roleId = 'j-fixed';
      expect(c.manualStep).toBe(10);
    });

    it('manual pickers step 30 min for an HOURLY job', () => {
      c.manualForm.roleId = 'j-hourly';
      expect(c.manualStep).toBe(30);
    });

    it('manual pickers default to 30 min with no job selected', () => {
      c.manualForm.roleId = '';
      expect(c.manualStep).toBe(30);
    });

    it('manual pickers default to 30 min for a legacy job without payType', () => {
      c.manualForm.roleId = 'j-legacy';
      expect(c.manualStep).toBe(30);
    });

    it('live start steps 10 min for a FIXED job (not hardcoded 30)', () => {
      c.liveForm.roleId = 'j-fixed';
      expect(c.liveStartStep).toBe(10);
    });

    it('live start steps 30 min for an HOURLY job', () => {
      c.liveForm.roleId = 'j-hourly';
      expect(c.liveStartStep).toBe(30);
    });

    it('live check-out steps 10 min when the active shift is a FIXED job', () => {
      c.activeLog.set({ roleName: 'OVERNIGHT 8H' } as never);
      expect(c.endStep).toBe(10);
    });

    it('live check-out steps 30 min when the active shift is an HOURLY job', () => {
      c.activeLog.set({ roleName: 'TABLE ROUNDS' } as never);
      expect(c.endStep).toBe(30);
    });

    it('live check-out defaults to 30 min when there is no active shift', () => {
      c.activeLog.set(null);
      expect(c.endStep).toBe(30);
    });
  });

  // ── Manual time snapping / seeding (fixes stray :07 minutes) ────────────────────
  describe('manual time snap + seed', () => {
    const jobs = [
      { id: 'j-fixed',  name: 'OVERNIGHT 8H', payType: 'fixed'  },
      { id: 'j-hourly', name: 'TABLE ROUNDS', payType: 'hourly' },
    ];
    let c: LogShiftComponent;

    beforeEach(() => {
      mockAuth.user = () => ({ id: 'u1', staffId: 'NC001', fullName: 'T', email: 't@test.com',
        roles: ['EMPLOYEE'], jobs });
      c = TestBed.createComponent(LogShiftComponent).componentInstance;
    });

    function hhmm(d: Date | null): string {
      return d ? `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}` : '';
    }

    it('snaps a picked start time to the nearest 30-min block for an HOURLY job', () => {
      c.manualForm.roleId = 'j-hourly';
      c.onManualStartChange(new Date(2026, 6, 4, 11, 7)); // 11:07 → 11:00
      expect(hhmm(c.manualFormStartTime)).toBe('11:00');
    });

    it('snaps a picked end time to the nearest 10-min block for a FIXED job', () => {
      c.manualForm.roleId = 'j-fixed';
      c.onManualEndChange(new Date(2026, 6, 4, 11, 7)); // 11:07 → 11:10
      expect(hhmm(c.manualFormEndTime)).toBe('11:10');
    });

    it('rolls 11:55 @30 up to 12:00 when snapping', () => {
      c.manualForm.roleId = 'j-hourly';
      c.onManualStartChange(new Date(2026, 6, 4, 11, 55));
      expect(hhmm(c.manualFormStartTime)).toBe('12:00');
    });

    it('clears the time when the picker is cleared to null', () => {
      c.manualForm.roleId = 'j-hourly';
      c.onManualStartChange(null);
      expect(c.manualFormStartTime).toBeNull();
    });

    it('seeds both pickers to block-aligned times when a job is chosen (start != end)', () => {
      c.manualForm.roleId = 'j-hourly';
      c.onManualJobChange();
      const s = c.manualFormStartTime!;
      const e = c.manualFormEndTime!;
      expect(s.getMinutes() % 30).toBe(0);
      expect(e.getMinutes() % 30).toBe(0);
      // end = start + one 30-min block → never a zero-duration seed
      expect(e.getTime()).toBeGreaterThan(s.getTime());
    });

    it('re-snaps already-picked times to the new block when the job changes (hourly → fixed)', () => {
      c.manualForm.roleId = 'j-hourly';
      c.manualFormStartTime = new Date(2026, 6, 4, 11, 30);
      c.manualFormEndTime   = new Date(2026, 6, 4, 12, 30);
      c.manualForm.roleId = 'j-fixed';
      c.onManualJobChange();
      // already on clean values → stay put, just re-aligned to the 10-min grid
      expect(hhmm(c.manualFormStartTime)).toBe('11:30');
      expect(hhmm(c.manualFormEndTime)).toBe('12:30');
    });
  });
});
