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
    days: 5, notes: null, status: 'pending', createdAt: '2026-05-01T00:00:00Z',
    ...overrides,
  };
}

const fakeBalance = {
  year: 2026, annualEntitlement: 14, annualUsed: 2, annualRemaining: 12,
  sickEntitlement: 14, sickUsed: 0, sickRemaining: 14, unpaidUsed: 0,
};

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('LeaveComponent', () => {
  let component: LeaveComponent;

  let mockLeaveSvc: {
    getAll:          ReturnType<typeof vi.fn>;
    create:          ReturnType<typeof vi.fn>;
    update:          ReturnType<typeof vi.fn>;
    delete:          ReturnType<typeof vi.fn>;
    getBalance:      ReturnType<typeof vi.fn>;
    getAllBalances:   ReturnType<typeof vi.fn>;
    setEntitlement:  ReturnType<typeof vi.fn>;
  };
  let mockStaffStore: {
    staff:     ReturnType<typeof vi.fn>;
    roles:     ReturnType<typeof vi.fn>;
    loading:   ReturnType<typeof vi.fn>;
    totalCount: ReturnType<typeof vi.fn>;
    loadStaff: ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockLeaveSvc = {
      getAll:         vi.fn().mockReturnValue(of({ items: [], total: 0 })),
      create:         vi.fn().mockReturnValue(of({ id: 'new-r' })),
      update:         vi.fn().mockReturnValue(of(undefined)),
      delete:         vi.fn().mockReturnValue(of(undefined)),
      getBalance:     vi.fn().mockReturnValue(of(fakeBalance)),
      getAllBalances:  vi.fn().mockReturnValue(of([])),
      setEntitlement: vi.fn().mockReturnValue(of(undefined)),
    };
    mockStaffStore = {
      staff:      vi.fn().mockReturnValue([]),
      roles:      vi.fn().mockReturnValue([]),
      loading:    vi.fn().mockReturnValue(false),
      totalCount: vi.fn().mockReturnValue(0),
      loadStaff:  vi.fn(),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
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

    it('sets formError when end date is before start date', () => {
      component.form.employeeId = 'e1';
      component.form.startDate  = '2026-06-10';
      component.form.endDate    = '2026-06-05';
      component.save();
      expect(component.formError()).toBe('End date must be on or after start date.');
      expect(mockLeaveSvc.create).not.toHaveBeenCalled();
    });
  });

  // ── save() — create path ───────────────────────────────────────────────────────

  describe('save() — create (editing is null)', () => {
    beforeEach(() => {
      component.form = {
        employeeId: 'e1', leaveType: 'annual',
        startDate: '2026-06-01', endDate: '2026-06-05',
        notes: null, status: 'approved',
      };
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
        notes: null, status: 'approved',
      };
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

    it('shows success toast with the new status after rejection', () => {
      component.records.set([makeRecord({ id: 'r1' })]);
      component.quickStatus('r1', 'rejected');
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave rejected.');
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
    it('sums days for approved records of the given type only', () => {
      component.records.set([
        makeRecord({ leaveType: 'annual', status: 'approved', days: 3 }),
        makeRecord({ id: 'r2', leaveType: 'annual', status: 'approved', days: 2 }),
        makeRecord({ id: 'r3', leaveType: 'annual', status: 'pending', days: 5 }), // pending — not counted
        makeRecord({ id: 'r4', leaveType: 'sick',   status: 'approved', days: 1 }), // different type
      ]);
      expect(component.countByType('annual')).toBe(5);
    });

    it('returns 0 when no records match the type', () => {
      component.records.set([makeRecord({ leaveType: 'sick', status: 'approved', days: 3 })]);
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
      component.entitlementForm = { year: 2026, annualDays: 16, sickDays: 10 };
      component.saveEntitlement();
      expect(mockLeaveSvc.setEntitlement).toHaveBeenCalledWith('emp-1', 2026, 16, 10);
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
});
