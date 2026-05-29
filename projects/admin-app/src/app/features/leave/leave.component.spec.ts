import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { LeaveComponent } from './leave.component';
import { LeaveService, StaffStore, ToastService, ConfirmService, LeaveRecord } from 'shared-lib';

/**
 * LeaveComponent — admin leave records management.
 *
 * WHY these tests matter:
 *  - save() date validation must reject end-before-start — an admin can inadvertently
 *    create a leave record with inverted dates if the guard is missing.
 *  - quickStatus() must find the record by id before calling update() — acting on a
 *    wrong or missing record corrupts leave history.
 *  - countByType() drives the summary pill counts; wrong totals mislead managers
 *    about remaining leave balances.
 *  - loadOverdue() filters by endDate < today — an off-by-one or timezone bug silently
 *    omits records that need manager action.
 *  - remove() requires explicit confirmation — deletion is permanent and affects payroll.
 */

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeRecord(overrides: Partial<LeaveRecord> = {}): LeaveRecord {
  return {
    id: 'r1', employeeId: 'e1', staffId: 'NC001', staffName: 'Jane Doe',
    leaveType: 'annual', startDate: '2026-06-01', endDate: '2026-06-05',
    startTime: '18:00', endTime: '02:00', hours: 8,
    notes: null, status: 'pending', createdAt: '2026-05-01T00:00:00Z',
    rejectionReason: null,
    ...overrides,
  };
}

const fakeBalance = {
  year: 2026, annualEntitlement: 14, annualUsed: 2, annualRemaining: 12,
  emergencyUsed: 0, carryOverHours: 0,
  sickEntitlement: 14, sickUsed: 0, sickRemaining: 14, unpaidUsed: 0,
};

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('LeaveComponent', () => {
  let component: LeaveComponent;

  let mockLeaveSvc: {
    getAll:               ReturnType<typeof vi.fn>;
    create:               ReturnType<typeof vi.fn>;
    update:               ReturnType<typeof vi.fn>;
    delete:               ReturnType<typeof vi.fn>;
    getBalance:           ReturnType<typeof vi.fn>;
    getAllBalances:        ReturnType<typeof vi.fn>;
    setEntitlement:       ReturnType<typeof vi.fn>;
    bulkSetEntitlement:   ReturnType<typeof vi.fn>;
      bulkUpdateStatus:     ReturnType<typeof vi.fn>;
    getSummary:           ReturnType<typeof vi.fn>;
    getPatternWarnings:   ReturnType<typeof vi.fn>;
  };
  let mockStaffStore: {
    staff:     ReturnType<typeof vi.fn>;
    roles:     ReturnType<typeof vi.fn>;
    loading:   ReturnType<typeof vi.fn>;
    totalCount: ReturnType<typeof vi.fn>;
    loadStaff: ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn>; warning: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockLeaveSvc = {
      getAll:               vi.fn().mockReturnValue(of({ items: [], total: 0 })),
      create:               vi.fn().mockReturnValue(of({ id: 'new-r' })),
      update:               vi.fn().mockReturnValue(of(undefined)),
      delete:               vi.fn().mockReturnValue(of(undefined)),
      getBalance:           vi.fn().mockReturnValue(of(fakeBalance)),
      getAllBalances:        vi.fn().mockReturnValue(of([])),
      setEntitlement:       vi.fn().mockReturnValue(of(undefined)),
      bulkSetEntitlement:   vi.fn().mockReturnValue(of({ count: 3 })),
      bulkUpdateStatus:     vi.fn().mockReturnValue(of({ count: 2 })),
      getSummary:           vi.fn().mockReturnValue(of([])),
      getPatternWarnings:   vi.fn().mockReturnValue(of([])),
    };
    mockStaffStore = {
      staff:      vi.fn().mockReturnValue([]),
      roles:      vi.fn().mockReturnValue([]),
      loading:    vi.fn().mockReturnValue(false),
      totalCount: vi.fn().mockReturnValue(0),
      loadStaff:  vi.fn(),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn(), warning: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [LeaveComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: LeaveService,       useValue: mockLeaveSvc },
        { provide: StaffStore,         useValue: mockStaffStore },
        { provide: ToastService,       useValue: mockToastSvc },
        { provide: ConfirmService,     useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(LeaveComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads staff list for the employee filter dropdown', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadStaff).toHaveBeenCalled();
    });

    it('loads leave records for the table', () => {
      component.ngOnInit();
      expect(mockLeaveSvc.getAll).toHaveBeenCalled();
    });

    it('loads overdue leaves for the alert banner', () => {
      component.ngOnInit();
      // getAll is called for records + for pending count + for overdue — at least 3 calls
      expect(mockLeaveSvc.getAll.mock.calls.length).toBeGreaterThanOrEqual(2);
    });
  });

  // ── loadOverdue() ─────────────────────────────────────────────────────────────

  describe('loadOverdue()', () => {
    it('requests only pending leaves for the overdue check', () => {
      component.loadOverdue();
      const call = mockLeaveSvc.getAll.mock.calls.find((c: any) => c[0]?.status === 'pending');
      expect(call).toBeDefined();
    });

    it('filters out records whose endDate is today or later — only past-due records are overdue', () => {
      const today     = component.todayIso;
      const yesterday = (() => {
        const d = new Date(today); d.setDate(d.getDate() - 1);
        return d.toISOString().slice(0, 10);
      })();
      const overduePast   = makeRecord({ endDate: yesterday, status: 'pending' });
      const notOverdueYet = makeRecord({ id: 'r2', endDate: today, status: 'pending' });

      mockLeaveSvc.getAll.mockReturnValue(of({ items: [overduePast, notOverdueYet], total: 2 }));
      component.loadOverdue();
      expect(component.overdueLeaves()).toEqual([overduePast]);
    });

    it('sets overdueLeaves to empty array when no pending records are overdue', () => {
      mockLeaveSvc.getAll.mockReturnValue(of({ items: [], total: 0 }));
      component.loadOverdue();
      expect(component.overdueLeaves()).toEqual([]);
    });
  });

  // ── save() — validation ───────────────────────────────────────────────────────

  describe('save() — validation', () => {
    it('sets formError and does not save when employeeId is missing', () => {
      component.form.employeeId = '';
      component.form.startDate  = '2026-06-01';
      component.form.endDate    = '2026-06-05';
      component.save();
      expect(component.formError()).toBe('Please fill in all required fields.');
      expect(mockLeaveSvc.create).not.toHaveBeenCalled();
    });

    it('sets formError and does not save when startDate is missing', () => {
      component.form.employeeId = 'e1';
      component.form.startDate  = '';
      component.form.endDate    = '2026-06-05';
      component.save();
      expect(component.formError()).toBe('Please fill in all required fields.');
    });

    it('sets formError when the start/end time pickers are not set', () => {
      component.form.employeeId = 'e1';
      component.form.startDate  = '2026-06-01';
      component.form.endDate    = '2026-06-05';
      component.form.hours      = null;
      // start/end time pickers are required by save() — none set here
      component.startTimeDate   = null;
      component.endTimeDate     = null;
      component.save();
      expect(component.formError()).toBe('Please fill in all required fields.');
      expect(mockLeaveSvc.create).not.toHaveBeenCalled();
    });

    it('sets formError when times produce negative hours (end before start)', () => {
      component.form.employeeId = 'e1';
      component.form.startDate  = '2026-06-01';
      component.form.endDate    = '2026-06-01';
      component.form.hours      = null;
      component.startTimeDate   = new Date(2026, 5, 1, 18, 0); // 6:00 PM
      component.endTimeDate     = new Date(2026, 5, 1, 10, 0); // 10:00 AM — before start
      component.save();
      expect(component.formError()).toBe('End date/time must be after start date/time.');
      expect(mockLeaveSvc.create).not.toHaveBeenCalled();
    });
  });

  // ── save() — create path ───────────────────────────────────────────────────────

  describe('save() — create (editing is null)', () => {
    beforeEach(() => {
      component.form = {
        employeeId: 'e1', leaveType: 'annual',
        startDate: '2026-06-01', endDate: '2026-06-05',
        startTime: null, endTime: null, hours: 8,
        notes: null, status: 'approved',
      };
      component.form.startTime = null;
      component.form.endTime   = null;
      // save() requires the time pickers; with a 4-day date span any valid
      // times yield positive hours so the create/update path is reached.
      component.startTimeDate = new Date(2026, 5, 1, 9, 0);
      component.endTimeDate   = new Date(2026, 5, 1, 17, 0);
      component.editing.set(null);
    });

    it('calls leaveSvc.create() with the form data', () => {
      component.save();
      expect(mockLeaveSvc.create).toHaveBeenCalledWith(
        expect.objectContaining({ employeeId: 'e1', startDate: '2026-06-01' }),
      );
    });

    it('shows success toast and hides form after create', () => {
      component.showForm.set(true);
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave saved.');
      expect(component.showForm()).toBe(false);
    });

    it('sets formError from API error on create failure', () => {
      mockLeaveSvc.create.mockReturnValue(
        throwError(() => ({ error: { error: 'Overlapping leave request.' } })),
      );
      component.save();
      expect(component.formError()).toBe('Overlapping leave request.');
    });

    it('clears saving flag after create error', () => {
      mockLeaveSvc.create.mockReturnValue(throwError(() => ({})));
      component.save();
      expect(component.saving()).toBe(false);
    });
  });

  // ── save() — update path ──────────────────────────────────────────────────────

  describe('save() — update (editing is set)', () => {
    beforeEach(() => {
      component.form = {
        employeeId: 'e1', leaveType: 'annual',
        startDate: '2026-06-01', endDate: '2026-06-05',
        startTime: null, endTime: null, hours: 8,
        notes: null, status: 'approved',
      };
      component.form.startTime = null;
      component.form.endTime   = null;
      // save() requires the time pickers (see create block above).
      component.startTimeDate = new Date(2026, 5, 1, 9, 0);
      component.endTimeDate   = new Date(2026, 5, 1, 17, 0);
      component.editing.set('r-existing');
    });

    it('calls leaveSvc.update() with the record id and form data', () => {
      component.save();
      expect(mockLeaveSvc.update).toHaveBeenCalledWith('r-existing', expect.objectContaining({ employeeId: 'e1' }));
    });

    it('does NOT call leaveSvc.create() when editing an existing record', () => {
      component.save();
      expect(mockLeaveSvc.create).not.toHaveBeenCalled();
    });

    it('shows success toast and clears editing state after update', () => {
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave saved.');
      expect(component.editing()).toBeNull();
    });
  });

  // ── quickStatus() ─────────────────────────────────────────────────────────────

  describe('quickStatus()', () => {
    it('does nothing when record id is not found', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.quickStatus('nonexistent', 'approved');
      expect(mockLeaveSvc.update).not.toHaveBeenCalled();
    });

    it('calls update() with the correct status when record is found', () => {
      component.records.set([makeRecord({ id: 'r1', status: 'pending' })]);
      component.quickStatus('r1', 'approved');
      expect(mockLeaveSvc.update).toHaveBeenCalledWith('r1', expect.objectContaining({ status: 'approved' }));
    });

    it('shows success toast with the new status after approval', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.quickStatus('r1', 'approved');
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave approved.');
    });

    it('routes rejection through startReject() — sets rejectingId instead of calling update directly', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.quickStatus('r1', 'rejected');
      expect(component.rejectingId()).toBe('r1');
      expect(mockLeaveSvc.update).not.toHaveBeenCalled();
    });

    it('shows error toast when status update fails', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      mockLeaveSvc.update.mockReturnValue(throwError(() => new Error('Network')));
      component.quickStatus('r1', 'approved');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Update failed.');
    });
  });

  // ── countByType() ─────────────────────────────────────────────────────────────

  describe('countByType()', () => {
    it('sums hours for approved records of the given type only', () => {
      component.records.set([
        makeRecord({ leaveType: 'annual', status: 'approved', hours: 6 }),
        makeRecord({ id: 'r2', leaveType: 'annual', status: 'approved', hours: 8 }),
        makeRecord({ id: 'r3', leaveType: 'annual', status: 'pending', hours: 4 }), // pending — not counted
        makeRecord({ id: 'r4', leaveType: 'sick',   status: 'approved', hours: 6 }), // different type
      ]);
      expect(component.countByType('annual')).toBe(14);
    });

    it('returns 0 when no records match the type', () => {
      component.records.set([makeRecord({ leaveType: 'sick', status: 'approved', hours: 6 })]);
      expect(component.countByType('annual')).toBe(0);
    });

    it('returns 0 when there are no records', () => {
      component.records.set([]);
      expect(component.countByType('annual')).toBe(0);
    });
  });

  // ── leaveTypeSeverity() ───────────────────────────────────────────────────────

  describe('leaveTypeSeverity()', () => {
    it('returns "success" for annual leave', () => {
      expect(component.leaveTypeSeverity('annual')).toBe('success');
    });
    it('returns "danger" for sick leave', () => {
      expect(component.leaveTypeSeverity('sick')).toBe('danger');
    });
    it('returns "secondary" for unpaid leave', () => {
      expect(component.leaveTypeSeverity('unpaid')).toBe('secondary');
    });
    it('returns "warn" for no-show', () => {
      expect(component.leaveTypeSeverity('no-show')).toBe('warn');
    });
    it('returns "warn" for emergency leave', () => {
      expect(component.leaveTypeSeverity('emergency')).toBe('warn');
    });
    it('returns "secondary" as default for unknown types', () => {
      expect(component.leaveTypeSeverity('unknown-type')).toBe('secondary');
    });
  });

  // ── remove() ─────────────────────────────────────────────────────────────────

  describe('remove()', () => {
    it('shows a confirm dialog before deletion', () => {
      component.remove('r1');
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls leaveSvc.delete when user confirms', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.remove('r1');
      expect(mockLeaveSvc.delete).toHaveBeenCalledWith('r1');
    });

    it('shows success toast after deletion', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.remove('r1');
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave deleted.');
    });

    it('does NOT call delete when user cancels', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.remove('r1');
      expect(mockLeaveSvc.delete).not.toHaveBeenCalled();
    });

    it('shows error toast when delete fails', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.delete.mockReturnValue(throwError(() => new Error('Network')));
      component.remove('r1');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Delete failed.');
    });
  });

  // ── openAdd() / startEdit() / cancelForm() ────────────────────────────────────

  describe('form panel management', () => {
    it('openAdd() shows the form and clears editing', () => {
      component.editing.set('existing-id');
      component.openAdd();
      expect(component.showForm()).toBe(true);
      expect(component.editing()).toBeNull();
    });

    it('startEdit() sets form to the record data and marks editing', () => {
      const r = makeRecord({ id: 'edit-r', employeeId: 'emp-2', leaveType: 'sick' });
      component.startEdit(r);
      expect(component.editing()).toBe('edit-r');
      expect(component.form.employeeId).toBe('emp-2');
      expect(component.form.leaveType).toBe('sick');
    });

    it('cancelForm() hides form and clears editing id', () => {
      component.showForm.set(true);
      component.editing.set('some-id');
      component.cancelForm();
      expect(component.showForm()).toBe(false);
      expect(component.editing()).toBeNull();
    });
  });

  // ── onStartDateChange() ───────────────────────────────────────────────────────

  describe('onStartDateChange()', () => {
    it('auto-fills endDate when endDate is empty', () => {
      component.form.endDate = '';
      component.onStartDateChange('2026-07-10');
      expect(component.form.endDate).toBe('2026-07-10');
    });

    it('updates endDate when new start is after current end', () => {
      component.form.endDate = '2026-07-05';
      component.onStartDateChange('2026-07-10');
      expect(component.form.endDate).toBe('2026-07-10');
    });

    it('leaves endDate unchanged when current end is on or after new start', () => {
      component.form.endDate = '2026-07-20';
      component.onStartDateChange('2026-07-10');
      expect(component.form.endDate).toBe('2026-07-20');
    });
  });

  // ── openCalLeave() ────────────────────────────────────────────────────────────

  describe('openCalLeave()', () => {
    it('selects the record start date as the calendar day (view mode, no edit form)', () => {
      const r = makeRecord({ startDate: '2026-07-10' });
      const fakeEvent = { stopPropagation: vi.fn() } as unknown as Event;
      component.openCalLeave(r, fakeEvent);
      expect(component.selectedCalDay()).toBe('2026-07-10');
      expect(component.showForm()).toBe(false);
    });

    it('calls stopPropagation to prevent day-cell click from also firing', () => {
      const r = makeRecord({ startDate: '2026-07-10' });
      const fakeEvent = { stopPropagation: vi.fn() } as unknown as Event;
      component.openCalLeave(r, fakeEvent);
      expect(fakeEvent.stopPropagation).toHaveBeenCalled();
    });
  });

  // ── Calendar navigation ───────────────────────────────────────────────────────

  describe('prevCalMonth() / nextCalMonth()', () => {
    it('prevCalMonth() decrements month and keeps year on non-January', () => {
      component.calMonth.set(5); // June
      component.calYear.set(2026);
      component.prevCalMonth();
      expect(component.calMonth()).toBe(4); // May
      expect(component.calYear()).toBe(2026);
    });

    it('prevCalMonth() wraps from January to December and decrements year', () => {
      component.calMonth.set(0); // January
      component.calYear.set(2026);
      component.prevCalMonth();
      expect(component.calMonth()).toBe(11); // December
      expect(component.calYear()).toBe(2025);
    });

    it('nextCalMonth() increments month and keeps year on non-December', () => {
      component.calMonth.set(5); // June
      component.calYear.set(2026);
      component.nextCalMonth();
      expect(component.calMonth()).toBe(6); // July
      expect(component.calYear()).toBe(2026);
    });

    it('nextCalMonth() wraps from December to January and increments year', () => {
      component.calMonth.set(11); // December
      component.calYear.set(2026);
      component.nextCalMonth();
      expect(component.calMonth()).toBe(0); // January
      expect(component.calYear()).toBe(2027);
    });
  });

  // ── saveEntitlement() ─────────────────────────────────────────────────────────

  describe('saveEntitlement()', () => {
    it('does nothing when no employee is filtered', () => {
      component.filterEmployeeId = '';
      component.saveEntitlement();
      expect(mockLeaveSvc.setEntitlement).not.toHaveBeenCalled();
    });

    it('calls setEntitlement with the form values when employee is filtered', () => {
      component.filterEmployeeId = 'emp-1';
      component.entitlementForm = { year: 2026, annualHours: 128, sickHours: 80 };
      component.saveEntitlement();
      expect(mockLeaveSvc.setEntitlement).toHaveBeenCalledWith('emp-1', 2026, 128, 80);
    });

    it('shows success toast and exits edit mode after saving', () => {
      component.filterEmployeeId = 'emp-1';
      component.saveEntitlement();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Entitlement updated.');
      expect(component.editingEntitlement()).toBe(false);
    });

    it('shows error toast when setEntitlement fails', () => {
      component.filterEmployeeId = 'emp-1';
      mockLeaveSvc.setEntitlement.mockReturnValue(throwError(() => new Error('Network')));
      component.saveEntitlement();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to update entitlement.');
    });
  });

  // ── Item 4: onFormEmployeeChange() ────────────────────────────────────────────

  describe('onFormEmployeeChange()', () => {
    it('loads and sets formBalance when employee is selected', () => {
      component.onFormEmployeeChange('emp-1');
      expect(mockLeaveSvc.getBalance).toHaveBeenCalledWith('emp-1');
      expect(component.formBalance()).toEqual(fakeBalance);
    });

    it('clears formBalance when employee is deselected (empty string)', () => {
      component.formBalance.set(fakeBalance);
      component.onFormEmployeeChange('');
      expect(component.formBalance()).toBeNull();
      expect(mockLeaveSvc.getBalance).not.toHaveBeenCalledWith('');
    });

    it('clears formBalance when getBalance fails', () => {
      component.formBalance.set(fakeBalance);
      mockLeaveSvc.getBalance.mockReturnValue(throwError(() => new Error('Network')));
      component.onFormEmployeeChange('emp-1');
      expect(component.formBalance()).toBeNull();
    });
  });

  // ── Item 5: bulkApplyEntitlement() ────────────────────────────────────────────

  describe('bulkApplyEntitlement()', () => {
    it('calls bulkSetEntitlement with entitlementForm values when user confirms', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.entitlementForm = { year: 2026, annualHours: 112, sickHours: 56 };
      component.bulkApplyEntitlement();
      expect(mockLeaveSvc.bulkSetEntitlement).toHaveBeenCalledWith(2026, 112, 56);
    });

    it('shows success toast with count on success', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.bulkApplyEntitlement();
      expect(mockToastSvc.success).toHaveBeenCalledWith(expect.stringContaining('3'));
    });

    it('closes the bulk entitlement panel on success', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.showBulkEntForm.set(true);
      component.bulkApplyEntitlement();
      expect(component.showBulkEntForm()).toBe(false);
    });

    it('does NOT call bulkSetEntitlement when user cancels', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.bulkApplyEntitlement();
      expect(mockLeaveSvc.bulkSetEntitlement).not.toHaveBeenCalled();
    });

    it('shows error toast when bulkSetEntitlement fails', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.bulkSetEntitlement.mockReturnValue(
        throwError(() => ({ error: { error: 'Year out of range.' } })));
      component.bulkApplyEntitlement();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Year out of range.');
    });
  });

  // ── Item 10: exportCsv() ──────────────────────────────────────────────────────

  describe('exportCsv()', () => {
    it('calls getAll with current filter params and pageSize 1000', () => {
      component.filterEmployeeId = 'emp-1';
      component.filterStatus     = 'approved';
      component.exportCsv();
      expect(mockLeaveSvc.getAll).toHaveBeenCalledWith(expect.objectContaining({
        employeeId: 'emp-1',
        status:     'approved',
        pageSize:   1000,
      }));
    });

    it('shows warning toast when there are no records to export', () => {
      mockLeaveSvc.getAll.mockReturnValue(of({ items: [], total: 0 }));
      component.exportCsv();
      expect(mockToastSvc.warning).toHaveBeenCalledWith('No records to export.');
    });

    it('shows success toast with record count when export succeeds', () => {
      const records = [makeRecord(), makeRecord({ id: 'r2' })];
      mockLeaveSvc.getAll.mockReturnValue(of({ items: records, total: 2 }));
      component.exportCsv();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Exported 2 records.');
    });

    it('shows error toast when getAll fails', () => {
      mockLeaveSvc.getAll.mockReturnValue(throwError(() => new Error('Network')));
      component.exportCsv();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Export failed.');
    });
  });

  // ── Item 12: prevBalanceYear() / nextBalanceYear() ────────────────────────────

  describe('prevBalanceYear() / nextBalanceYear()', () => {
    it('prevBalanceYear() decrements balanceYear by 1', () => {
      component.balanceYear.set(2026);
      component.prevBalanceYear();
      expect(component.balanceYear()).toBe(2025);
    });

    it('nextBalanceYear() increments balanceYear by 1', () => {
      component.balanceYear.set(2026);
      component.nextBalanceYear();
      expect(component.balanceYear()).toBe(2027);
    });

    it('prevBalanceYear() triggers a balance reload', () => {
      const callsBefore = mockLeaveSvc.getAllBalances.mock.calls.length;
      component.prevBalanceYear();
      expect(mockLeaveSvc.getAllBalances.mock.calls.length).toBeGreaterThan(callsBefore);
    });

    it('nextBalanceYear() triggers a balance reload', () => {
      const callsBefore = mockLeaveSvc.getAllBalances.mock.calls.length;
      component.nextBalanceYear();
      expect(mockLeaveSvc.getAllBalances.mock.calls.length).toBeGreaterThan(callsBefore);
    });

    it('prevBalanceYear() also loads the leave summary for the new year', () => {
      component.balanceYear.set(2026);
      component.prevBalanceYear();
      expect(mockLeaveSvc.getSummary).toHaveBeenCalledWith(2025);
    });

    it('nextBalanceYear() also loads the leave summary for the new year', () => {
      component.balanceYear.set(2026);
      component.nextBalanceYear();
      expect(mockLeaveSvc.getSummary).toHaveBeenCalledWith(2027);
    });
  });

  // ── Item 10: statusSeverity() ─────────────────────────────────────────────────

  describe('statusSeverity()', () => {
    it('returns "success" for approved', () => {
      expect(component.statusSeverity('approved')).toBe('success');
    });

    it('returns "warn" for pending', () => {
      expect(component.statusSeverity('pending')).toBe('warn');
    });

    it('returns "danger" for rejected', () => {
      expect(component.statusSeverity('rejected')).toBe('danger');
    });

    it('returns "secondary" for cancelled — distinguishes cancellation from rejection visually', () => {
      expect(component.statusSeverity('cancelled')).toBe('secondary');
    });

    it('returns "secondary" as fallback for unknown status strings', () => {
      expect(component.statusSeverity('unknown-status')).toBe('secondary');
    });
  });

  // ── Item 1: startReject() / cancelReject() / confirmReject() ─────────────────

  describe('startReject() / cancelReject() / confirmReject()', () => {
    it('startReject() sets rejectingId to the record id — opens inline rejection form', () => {
      const r = makeRecord({ id: 'r-rej' });
      component.startReject(r);
      expect(component.rejectingId()).toBe('r-rej');
    });

    it('startReject() clears any stale rejection input from a previous attempt', () => {
      component.rejectionInput.set('old reason');
      component.startReject(makeRecord());
      expect(component.rejectionInput()).toBe('');
    });

    it('cancelReject() clears rejectingId so the inline form disappears', () => {
      component.rejectingId.set('r1');
      component.cancelReject();
      expect(component.rejectingId()).toBeNull();
    });

    it('cancelReject() also clears rejectionInput', () => {
      component.rejectionInput.set('some reason');
      component.cancelReject();
      expect(component.rejectionInput()).toBe('');
    });

    it('confirmReject() calls leaveSvc.update with status "rejected" and the entered reason', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.rejectingId.set('r1');
      component.rejectionInput.set('No annual leave balance');
      component.confirmReject('r1');
      expect(mockLeaveSvc.update).toHaveBeenCalledWith('r1', expect.objectContaining({
        status: 'rejected',
        rejectionReason: 'No annual leave balance',
      }));
    });

    it('confirmReject() clears rejectingId immediately before calling update', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.rejectingId.set('r1');
      component.confirmReject('r1');
      expect(component.rejectingId()).toBeNull();
    });

    it('confirmReject() shows success toast after update completes', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.confirmReject('r1');
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave rejected.');
    });
  });

  // ── Item 3: bulk selection ────────────────────────────────────────────────────

  describe('bulk selection', () => {
    it('toggleBulkMode() enables bulk-select mode', () => {
      component.bulkSelecting.set(false);
      component.toggleBulkMode();
      expect(component.bulkSelecting()).toBe(true);
    });

    it('toggleBulkMode() clears selectedIds and reason when turning off', () => {
      component.bulkSelecting.set(true);
      component.selectedIds.set(new Set(['r1', 'r2']));
      component.bulkRejReason.set('N/A');
      component.toggleBulkMode();
      expect(component.selectedIds().size).toBe(0);
      expect(component.bulkRejReason()).toBe('');
    });

    it('toggleSelect() adds an id to selectedIds when not already selected', () => {
      component.toggleSelect('r1');
      expect(component.selectedIds().has('r1')).toBe(true);
    });

    it('toggleSelect() removes an id from selectedIds when already selected', () => {
      component.selectedIds.set(new Set(['r1']));
      component.toggleSelect('r1');
      expect(component.selectedIds().has('r1')).toBe(false);
    });

    it('toggleSelectAll() selects all pending records — skips non-pending', () => {
      component.records.set([
        makeRecord({ id: 'r1', status: 'pending' }),
        makeRecord({ id: 'r2', status: 'pending' }),
        makeRecord({ id: 'r3', status: 'approved' }),
      ]);
      component.toggleSelectAll();
      expect(component.selectedIds().has('r1')).toBe(true);
      expect(component.selectedIds().has('r2')).toBe(true);
      expect(component.selectedIds().has('r3')).toBe(false);
    });

    it('toggleSelectAll() deselects all pending records when all are already selected', () => {
      component.records.set([
        makeRecord({ id: 'r1', status: 'pending' }),
        makeRecord({ id: 'r2', status: 'pending' }),
      ]);
      component.selectedIds.set(new Set(['r1', 'r2']));
      component.toggleSelectAll();
      expect(component.selectedIds().has('r1')).toBe(false);
      expect(component.selectedIds().has('r2')).toBe(false);
    });

    it('allVisibleSelected() returns true when all pending records are selected', () => {
      component.records.set([makeRecord({ id: 'r1', status: 'pending' })]);
      component.selectedIds.set(new Set(['r1']));
      expect(component.allVisibleSelected()).toBe(true);
    });

    it('allVisibleSelected() returns false when no pending records are selected', () => {
      component.records.set([makeRecord({ id: 'r1', status: 'pending' })]);
      component.selectedIds.set(new Set());
      expect(component.allVisibleSelected()).toBe(false);
    });
  });

  // ── Item 3: bulkApproveSelected() ────────────────────────────────────────────

  describe('bulkApproveSelected()', () => {
    it('does nothing when selectedIds is empty — avoids spurious API call', () => {
      component.selectedIds.set(new Set());
      component.bulkApproveSelected();
      expect(mockConfirmSvc.confirm).not.toHaveBeenCalled();
    });

    it('calls leaveSvc.bulkUpdateStatus with status "approved" for selected ids', () => {
      component.selectedIds.set(new Set(['r1', 'r2']));
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.bulkApproveSelected();
      expect(mockLeaveSvc.bulkUpdateStatus).toHaveBeenCalledWith(
        expect.arrayContaining(['r1', 'r2']), 'approved',
      );
    });

    it('shows success toast with count after bulk approval', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.bulkUpdateStatus.mockReturnValue(of({ count: 1 }));
      component.bulkApproveSelected();
      expect(mockToastSvc.success).toHaveBeenCalledWith(expect.stringContaining('1'));
    });

    it('clears selectedIds after successful bulk approval', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.bulkApproveSelected();
      expect(component.selectedIds().size).toBe(0);
    });

    it('does NOT call bulkUpdateStatus when user cancels the confirm dialog', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.bulkApproveSelected();
      expect(mockLeaveSvc.bulkUpdateStatus).not.toHaveBeenCalled();
    });

    it('shows error toast when bulkUpdateStatus fails', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.bulkUpdateStatus.mockReturnValue(
        throwError(() => ({ error: { error: 'Bulk approve failed.' } })));
      component.bulkApproveSelected();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Bulk approve failed.');
    });
  });

  // ── Item 3: bulkRejectSelected() ─────────────────────────────────────────────

  describe('bulkRejectSelected()', () => {
    it('does nothing when selectedIds is empty', () => {
      component.selectedIds.set(new Set());
      component.bulkRejectSelected();
      expect(mockConfirmSvc.confirm).not.toHaveBeenCalled();
    });

    it('calls leaveSvc.bulkUpdateStatus with status "rejected" and the entered reason', () => {
      component.selectedIds.set(new Set(['r1']));
      component.bulkRejReason.set('No balance');
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.bulkRejectSelected();
      expect(mockLeaveSvc.bulkUpdateStatus).toHaveBeenCalledWith(['r1'], 'rejected', 'No balance');
    });

    it('passes undefined reason when bulkRejReason is blank', () => {
      component.selectedIds.set(new Set(['r1']));
      component.bulkRejReason.set('');
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.bulkRejectSelected();
      expect(mockLeaveSvc.bulkUpdateStatus).toHaveBeenCalledWith(['r1'], 'rejected', undefined);
    });

    it('shows success toast with count on success', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.bulkUpdateStatus.mockReturnValue(of({ count: 1 }));
      component.bulkRejectSelected();
      expect(mockToastSvc.success).toHaveBeenCalledWith(expect.stringContaining('1'));
    });

    it('does NOT call bulkUpdateStatus when user cancels', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.bulkRejectSelected();
      expect(mockLeaveSvc.bulkUpdateStatus).not.toHaveBeenCalled();
    });

    it('shows error toast when bulkUpdateStatus fails', () => {
      component.selectedIds.set(new Set(['r1']));
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.bulkUpdateStatus.mockReturnValue(
        throwError(() => ({ error: { error: 'Bulk reject failed.' } })));
      component.bulkRejectSelected();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Bulk reject failed.');
    });
  });

  // ── Item 15: markNoShow() calls loadBalances() ────────────────────────────────

  describe('markNoShow()', () => {
    it('calls leaveSvc.update with leaveType "no-show" and status "approved"', () => {
      const r = makeRecord({ id: 'r1', leaveType: 'annual' });
      component.markNoShow(r);
      expect(mockLeaveSvc.update).toHaveBeenCalledWith('r1', expect.objectContaining({
        leaveType: 'no-show',
        status: 'approved',
      }));
    });

    it('calls loadBalances() after success — item 15: balances must refresh after no-show', () => {
      mockLeaveSvc.update.mockReturnValue(of(undefined));
      const callsBefore = mockLeaveSvc.getAllBalances.mock.calls.length;
      component.markNoShow(makeRecord());
      expect(mockLeaveSvc.getAllBalances.mock.calls.length).toBeGreaterThan(callsBefore);
    });

    it('shows success toast "Marked as no-show." after update succeeds', () => {
      component.markNoShow(makeRecord());
      expect(mockToastSvc.success).toHaveBeenCalledWith('Marked as no-show.');
    });

    it('shows error toast when update fails', () => {
      mockLeaveSvc.update.mockReturnValue(throwError(() => new Error('Network')));
      component.markNoShow(makeRecord());
      expect(mockToastSvc.error).toHaveBeenCalledWith('Update failed.');
    });
  });

  // ── Item 12: patternWarningFor() ─────────────────────────────────────────────

  describe('patternWarningFor()', () => {
    it('returns undefined when no pattern warning exists for the employee', () => {
      component.patternWarnings.set([]);
      expect(component.patternWarningFor('emp-1')).toBeUndefined();
    });

    it('returns the matching warning when an employee has ≥3 sick leaves in 90 days', () => {
      const warning = { employeeId: 'emp-1', staffId: 'NC001', fullName: 'Jane Doe', sickLeaveCount: 4, sickLeaveHours: 32 };
      component.patternWarnings.set([warning]);
      expect(component.patternWarningFor('emp-1')).toEqual(warning);
    });

    it('returns undefined when the warning belongs to a different employee', () => {
      const warning = { employeeId: 'emp-2', staffId: 'NC002', fullName: 'John Smith', sickLeaveCount: 3, sickLeaveHours: 24 };
      component.patternWarnings.set([warning]);
      expect(component.patternWarningFor('emp-1')).toBeUndefined();
    });
  });

  // ── Item 11: loadLeaveSummary() ───────────────────────────────────────────────

  describe('loadLeaveSummary()', () => {
    it('calls leaveSvc.getSummary with the current balanceYear', () => {
      component.balanceYear.set(2025);
      component.loadLeaveSummary();
      expect(mockLeaveSvc.getSummary).toHaveBeenCalledWith(2025);
    });

    it('populates leaveSummary signal with the fetched data', () => {
      const summary = [{
        employeeId: 'e1', staffId: 'NC001', fullName: 'Jane Doe',
        year: 2026, annualUsed: 8, sickUsed: 4, emergencyUsed: 0,
        unpaidUsed: 0, noShowUsed: 0, totalUsed: 12,
      }];
      mockLeaveSvc.getSummary.mockReturnValue(of(summary));
      component.loadLeaveSummary();
      expect(component.leaveSummary()).toEqual(summary);
    });

    it('sets leaveSummaryLoading to false after loading completes', () => {
      component.loadLeaveSummary();
      expect(component.leaveSummaryLoading()).toBe(false);
    });
  });

  // ── Item 12: loadPatternWarnings() ───────────────────────────────────────────

  describe('loadPatternWarnings()', () => {
    it('calls leaveSvc.getPatternWarnings to detect frequent sick-leave patterns', () => {
      component.loadPatternWarnings();
      expect(mockLeaveSvc.getPatternWarnings).toHaveBeenCalled();
    });

    it('populates patternWarnings signal with the fetched data', () => {
      const warnings = [{
        employeeId: 'e1', staffId: 'NC001', fullName: 'Jane Doe',
        sickLeaveCount: 3, sickLeaveHours: 24,
      }];
      mockLeaveSvc.getPatternWarnings.mockReturnValue(of(warnings));
      component.loadPatternWarnings();
      expect(component.patternWarnings()).toEqual(warnings);
    });

    it('sets patternLoading to false after loading completes', () => {
      component.loadPatternWarnings();
      expect(component.patternLoading()).toBe(false);
    });
  });

  // ── Item 4: presetTypes includes no-show ─────────────────────────────────────

  describe('presetTypes', () => {
    it('includes a "no-show" type filter entry for the Records tab', () => {
      const types = component.presetTypes.map(p => p.value);
      expect(types).toContain('no-show');
    });
  });

  // ── setDatePreset() ───────────────────────────────────────────────────────────

  describe('setDatePreset()', () => {
    beforeEach(() => {
      // Pin system clock to 2026-07-15 (Wednesday, month index 6)
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 6, 15));
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('"today" sets filterFrom and filterTo to today', () => {
      component.setDatePreset('today');
      expect(component.filterFrom).toBe('2026-07-15');
      expect(component.filterTo).toBe('2026-07-15');
    });

    it('"week" sets filterFrom to Monday of the current week', () => {
      // 2026-07-15 is Wednesday → Monday is 2026-07-13
      component.setDatePreset('week');
      expect(component.filterFrom).toBe('2026-07-13');
      expect(component.filterTo).toBe('2026-07-15');
    });

    it('"month" sets filterFrom to first of current month', () => {
      component.setDatePreset('month');
      expect(component.filterFrom).toBe('2026-07-01');
      expect(component.filterTo).toBe('2026-07-15');
    });

    it('"last-month" sets filterFrom and filterTo to full previous month', () => {
      // Current month is July 2026 → last month is June 2026
      component.setDatePreset('last-month');
      expect(component.filterFrom).toBe('2026-06-01');
      expect(component.filterTo).toBe('2026-06-30');
    });

    it('"last-3-months" sets filterFrom to 3 months ago (first of that month)', () => {
      // 2026-07 minus 3 = 2026-04
      component.setDatePreset('last-3-months');
      expect(component.filterFrom).toBe('2026-04-01');
      expect(component.filterTo).toBe('2026-07-15');
    });

    it('calls load(1) after setting any preset — so results update immediately', () => {
      const loadCallsBefore = mockLeaveSvc.getAll.mock.calls.length;
      component.setDatePreset('month');
      expect(mockLeaveSvc.getAll.mock.calls.length).toBeGreaterThan(loadCallsBefore);
    });
  });
});
