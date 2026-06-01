import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { StaffComponent } from './staff.component';
import {
  AuthService, StaffStore, ToastService, ConfirmService,
  Staff, StaffRole,
} from 'shared-lib';

/**
 * StaffComponent — admin staff management page.
 *
 * WHY these tests matter:
 *  - isFormValid gates the Save button; a wrong formula can let invalid data reach the API,
 *    or block valid submissions and frustrate admins.
 *  - ngOnInit must load both staff list and role options so the form is usable on arrival.
 *  - The search subject is debounced — firing too early or too late causes UX issues.
 *  - saveStaff() validation prevents missing identity docs (IC/passport) and DOB,
 *    which are required for payroll and compliance.
 *  - deleteStaff() and toggleActive() (deactivate path) are irreversible — they require
 *    explicit confirmation to prevent accidental data loss.
 */

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeStaff(overrides: Partial<Staff> = {}): Staff {
  return {
    id: 's1', staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
    fullName: 'Jane Doe', email: 'jane@test.com', roles: ['employee'], locationIds: [],
    isActive: true, phoneNumber: null, icNo: 'IC12345', passportNo: null,
    dateOfBirth: '1990-01-01', age: 36,
    ...overrides,
  };
}

function makeRole(name: string, isSystem = false): StaffRole {
  return { name, isSystem, hourlyRate: 10 };
}

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('StaffComponent', () => {
  let component: StaffComponent;

  let mockStaffStore: {
    staff:        ReturnType<typeof vi.fn>;
    roles:        ReturnType<typeof vi.fn>;
    loading:      ReturnType<typeof vi.fn>;
    totalCount:   ReturnType<typeof vi.fn>;
    loadStaff:    ReturnType<typeof vi.fn>;
    loadRoles:    ReturnType<typeof vi.fn>;
    register:     ReturnType<typeof vi.fn>;
    toggleActive: ReturnType<typeof vi.fn>;
    deleteStaff:  ReturnType<typeof vi.fn>;
    resetPassword: ReturnType<typeof vi.fn>;
    bulkAssignLocations: ReturnType<typeof vi.fn>;
    bulkAssignRoles:     ReturnType<typeof vi.fn>;
    bulkDeactivate:      ReturnType<typeof vi.fn>;
  };
  let mockAuth:       { user: () => any; hasRole: ReturnType<typeof vi.fn> };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockStaffStore = {
      staff:         vi.fn().mockReturnValue([]),
      roles:         vi.fn().mockReturnValue([]),
      loading:       vi.fn().mockReturnValue(false),
      totalCount:    vi.fn().mockReturnValue(0),
      loadStaff:     vi.fn(),
      loadRoles:     vi.fn(),
      register:      vi.fn().mockReturnValue(of(makeStaff())),
      toggleActive:  vi.fn().mockReturnValue(of(undefined)),
      deleteStaff:   vi.fn().mockReturnValue(of(undefined)),
      resetPassword: vi.fn().mockReturnValue(of(undefined)),
      bulkAssignLocations: vi.fn().mockReturnValue(of({ count: 2 })),
      bulkAssignRoles:     vi.fn().mockReturnValue(of({ count: 2 })),
      bulkDeactivate:      vi.fn().mockReturnValue(of({ count: 2 })),
    };
    mockAuth       = { user: () => null, hasRole: vi.fn().mockReturnValue(false) };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [StaffComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: StaffStore,         useValue: mockStaffStore },
        { provide: AuthService,        useValue: mockAuth },
        { provide: ToastService,       useValue: mockToastSvc },
        { provide: ConfirmService,     useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StaffComponent);
    component = fixture.componentInstance;
    // Do NOT call detectChanges() — avoids PrimeNG template initialisation errors.
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  // ── isFormValid getter ────────────────────────────────────────────────────────

  describe('isFormValid', () => {
    /** Fill the minimum required fields — staffId, firstName, password. */
    function fillValidForm(): void {
      component.newStaff.staffId  = 'NC999';
      component.newStaff.firstName = 'Alice';
      component.newStaff.password  = 'Password1!';   // ≥8 chars required by isFormValid
    }

    it('returns true with only staffId, firstName, and password filled', () => {
      fillValidForm();
      expect(component.isFormValid).toBe(true);
    });

    // ── per-field inline validation ──────────────────────────────────────────
    it('fieldError stays empty until the field is blurred (no nagging before interaction)', () => {
      component.newStaff.staffId = '';
      expect(component.fieldError('staffId')).toBe('');   // not touched yet
      component.markTouched('staffId');
      expect(component.fieldError('staffId')).toBe('staff.errStaffIdRequired');
    });

    it('fieldError flags an invalid email but accepts a blank one (optional)', () => {
      component.markTouched('email');
      component.newStaff.email = 'not-an-email';
      expect(component.fieldError('email')).toBe('staff.errEmailInvalid');
      component.newStaff.email = '';
      expect(component.fieldError('email')).toBe('');
      component.newStaff.email = 'ok@test.com';
      expect(component.fieldError('email')).toBe('');
    });

    it('fieldError clears once a required field is filled', () => {
      component.markTouched('firstName');
      expect(component.fieldError('firstName')).toBe('staff.errFirstNameRequired');
      component.newStaff.firstName = 'Alice';
      expect(component.fieldError('firstName')).toBe('');
    });

    it('returns false when staffId is empty', () => {
      fillValidForm();
      component.newStaff.staffId = '';
      expect(component.isFormValid).toBe(false);
    });

    it('returns false when staffId is whitespace only', () => {
      fillValidForm();
      component.newStaff.staffId = '   ';
      expect(component.isFormValid).toBe(false);
    });

    it('returns false when firstName is empty', () => {
      fillValidForm();
      component.newStaff.firstName = '';
      expect(component.isFormValid).toBe(false);
    });

    it('returns true when lastName is empty — lastName is optional', () => {
      fillValidForm();
      component.newStaff.lastName = '';
      expect(component.isFormValid).toBe(true);
    });

    it('returns true when email is null — email is optional', () => {
      fillValidForm();
      component.newStaff.email = null;
      expect(component.isFormValid).toBe(true);
    });

    it('returns true when both icNo and passportNo are null — identity docs are optional', () => {
      fillValidForm();
      component.newStaff.icNo       = null;
      component.newStaff.passportNo = null;
      expect(component.isFormValid).toBe(true);
    });

    it('returns true when dateOfBirth is null — DOB is optional', () => {
      fillValidForm();
      component.newStaff.dateOfBirth = null;
      expect(component.isFormValid).toBe(true);
    });

    it('returns false when password is shorter than 8 characters', () => {
      fillValidForm();
      component.newStaff.password = 'short';
      expect(component.isFormValid).toBe(false);
    });
  });

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads staff list on init so the table is populated immediately', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadStaff).toHaveBeenCalledWith('', 1, 10);
    });

    it('loads roles on init so the form role selector is populated', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadRoles).toHaveBeenCalled();
    });

    it('creates a searchSub subscription (not null)', () => {
      component.ngOnInit();
      expect((component as any).searchSub).toBeDefined();
    });
  });

  // ── onSearch() and debounce ───────────────────────────────────────────────────

  describe('onSearch()', () => {
    it('updates the search signal immediately', () => {
      component.ngOnInit();
      component.onSearch('alice');
      expect(component.search()).toBe('alice');
    });

    it('resets currentPage to 1 on each search — prevents browsing a non-existent page', () => {
      component.ngOnInit();
      component.currentPage.set(5);
      component.onSearch('alice');
      expect(component.currentPage()).toBe(1);
    });

    it('calls loadStaff with the search term after 300ms debounce', async () => {
      vi.useFakeTimers();
      component.ngOnInit();
      mockStaffStore.loadStaff.mockClear();
      component.onSearch('alice');
      vi.advanceTimersByTime(300);
      expect(mockStaffStore.loadStaff).toHaveBeenCalledWith('alice', 1, 10);
    });

    it('does NOT call loadStaff before the 300ms debounce elapses', () => {
      vi.useFakeTimers();
      component.ngOnInit();
      mockStaffStore.loadStaff.mockClear();
      component.onSearch('alice');
      vi.advanceTimersByTime(299);
      expect(mockStaffStore.loadStaff).not.toHaveBeenCalled();
    });

    it('fires only once for rapid repeated calls (debounce collapses them)', () => {
      vi.useFakeTimers();
      component.ngOnInit();
      mockStaffStore.loadStaff.mockClear();
      component.onSearch('a');
      vi.advanceTimersByTime(100);
      component.onSearch('al');
      vi.advanceTimersByTime(100);
      component.onSearch('alice');
      vi.advanceTimersByTime(300);
      expect(mockStaffStore.loadStaff).toHaveBeenCalledTimes(1);
      expect(mockStaffStore.loadStaff).toHaveBeenCalledWith('alice', 1, 10);
    });
  });

  // ── loadPage() ────────────────────────────────────────────────────────────────

  describe('loadPage()', () => {
    it('updates currentPage signal and fetches the correct page from the store', () => {
      component.ngOnInit();
      component.onSearch('test');
      mockStaffStore.loadStaff.mockClear();
      component.loadPage(3);
      expect(component.currentPage()).toBe(3);
      expect(mockStaffStore.loadStaff).toHaveBeenCalledWith('test', 3, 10);
    });
  });

  // ── saveStaff() — validation ──────────────────────────────────────────────────

  describe('saveStaff() — validation', () => {
    it('sets error for an invalid email format', () => {
      component.newStaff.staffId   = 'NC001';
      component.newStaff.firstName = 'Alice';
      component.newStaff.password  = 'Password1!';
      component.newStaff.email     = 'not-an-email';
      component.saveStaff();
      expect(component.error()).toBe('staff.errEmailInvalidFull');
      expect(mockStaffStore.register).not.toHaveBeenCalled();
    });

    it('does not error when email is null — email is optional', () => {
      mockStaffStore.roles.mockReturnValue([makeRole('employee')]);
      component.newStaff = {
        staffId: 'NC001', firstName: 'Alice', lastName: '', email: null,
        password: 'Password1!', roles: ['employee'], locations: [],
        phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
      };
      component.saveStaff();
      expect(component.error()).toBe('');
      expect(mockStaffStore.register).toHaveBeenCalled();
    });

    it('auto-assigns the first available role when roles array is empty', () => {
      mockStaffStore.roles.mockReturnValue([makeRole('employee')]);
      component.newStaff = {
        staffId: 'NC001', firstName: 'Alice', lastName: '', email: null,
        password: 'Password1!', roles: [], locations: [],
        phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
      };
      component.saveStaff();
      const req = mockStaffStore.register.mock.calls[0]?.[0];
      expect(req?.roles).toEqual(['employee']);
    });

    it('sets error when roles is empty and no available roles exist', () => {
      // roles() already returns [] by default
      component.newStaff = {
        staffId: 'NC001', firstName: 'Alice', lastName: '', email: null,
        password: 'Password1!', roles: [], locations: [],
        phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
      };
      component.saveStaff();
      expect(component.error()).toBe('staff.errSelectRole');
    });
  });

  // ── saveStaff() — success and error ──────────────────────────────────────────

  describe('saveStaff() — success', () => {
    function fillAndSubmit(): void {
      component.newStaff = {
        staffId: 'NC001', firstName: 'Alice', lastName: '', email: null,
        password: 'Password1!', roles: ['employee'], locations: [],
        phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
      };
      component.saveStaff();
    }

    it('calls register() with the form data', () => {
      fillAndSubmit();
      expect(mockStaffStore.register).toHaveBeenCalledWith(
        expect.objectContaining({ staffId: 'NC001', email: null }),
      );
    });

    it('shows success toast after registering a new staff member', () => {
      fillAndSubmit();
      expect(mockToastSvc.success).toHaveBeenCalledWith('staff.registered');
    });

    it('hides the form after successful registration', () => {
      component.showForm.set(true);
      fillAndSubmit();
      expect(component.showForm()).toBe(false);
    });

    it('resets the form fields after success', () => {
      fillAndSubmit();
      expect(component.newStaff.staffId).toBe('');
      expect(component.newStaff.email).toBeNull();
    });

    it('clears saving flag after success', () => {
      fillAndSubmit();
      expect(component.saving()).toBe(false);
    });

    it('reloads page 1 and clears search after successful registration', () => {
      component.search.set('old-search');
      mockStaffStore.loadStaff.mockClear();
      fillAndSubmit();
      expect(component.search()).toBe('');
      expect(mockStaffStore.loadStaff).toHaveBeenCalledWith('', 1, 10);
    });
  });

  describe('saveStaff() — API error', () => {
    beforeEach(() => {
      component.newStaff = {
        staffId: 'NC001', firstName: 'Alice', lastName: '', email: null,
        password: 'Password1!', roles: ['employee'], locations: [],
        phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
      };
    });

    it('shows the API error in the form error signal', () => {
      mockStaffStore.register.mockReturnValue(
        throwError(() => ({ error: { error: 'Staff ID already exists.' } })),
      );
      component.saveStaff();
      expect(component.error()).toBe('Staff ID already exists.');
    });

    it('shows error toast on API failure', () => {
      mockStaffStore.register.mockReturnValue(
        throwError(() => ({ error: { error: 'Staff ID already exists.' } })),
      );
      component.saveStaff();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Staff ID already exists.');
    });

    it('clears saving flag after error so the button is re-enabled', () => {
      mockStaffStore.register.mockReturnValue(throwError(() => ({})));
      component.saveStaff();
      expect(component.saving()).toBe(false);
    });
  });

  // ── openAdd() / closeForm() ───────────────────────────────────────────────────

  describe('form panel open/close', () => {
    it('openAdd() shows the form panel', () => {
      component.openAdd();
      expect(component.showForm()).toBe(true);
    });

    it('closeForm() hides the form and clears error', () => {
      component.showForm.set(true);
      component.error.set('some error');
      component.closeForm();
      expect(component.showForm()).toBe(false);
      expect(component.error()).toBe('');
    });

    it('closeForm() resets newStaff to defaults', () => {
      component.newStaff.staffId   = 'NC123';
      component.newStaff.password  = 'SomePass1!';
      component.closeForm();
      expect(component.newStaff.staffId).toBe('');
      expect(component.newStaff.password).toBe('');   // password cleared on close
    });
  });

  // ── deleteStaff() ─────────────────────────────────────────────────────────────

  describe('deleteStaff()', () => {
    it('shows a confirm dialog before deleting', () => {
      component.deleteStaff(makeStaff());
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('uses danger style in the confirm dialog', () => {
      component.deleteStaff(makeStaff());
      expect(mockConfirmSvc.confirm.mock.calls[0][0].danger).toBe(true);
    });

    it('calls store.deleteStaff with the staff id when confirmed', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.deleteStaff(makeStaff({ id: 'del-1' }));
      expect(mockStaffStore.deleteStaff).toHaveBeenCalledWith('del-1');
    });

    it('shows success toast after deletion', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.ngOnInit();
      component.deleteStaff(makeStaff({ fullName: 'Jane Doe' }));
      expect(mockToastSvc.success).toHaveBeenCalledWith('staff.deletedToast');
    });

    it('does NOT call store.deleteStaff when user cancels the dialog', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.deleteStaff(makeStaff());
      expect(mockStaffStore.deleteStaff).not.toHaveBeenCalled();
    });

    it('shows error toast when delete API call fails', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockStaffStore.deleteStaff.mockReturnValue(
        throwError(() => ({ error: { error: 'Cannot delete: shift history exists.' } })),
      );
      component.deleteStaff(makeStaff());
      expect(mockToastSvc.error).toHaveBeenCalledWith('Cannot delete: shift history exists.');
    });
  });

  // ── toggleActive() ────────────────────────────────────────────────────────────

  describe('toggleActive()', () => {
    it('shows confirm dialog before DEACTIVATING an active staff member', () => {
      component.toggleActive('s1', true);
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls store.toggleActive when deactivation is confirmed', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.toggleActive('s1', true);
      expect(mockStaffStore.toggleActive).toHaveBeenCalledWith('s1', true);
    });

    it('does NOT call store.toggleActive when deactivation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.toggleActive('s1', true);
      expect(mockStaffStore.toggleActive).not.toHaveBeenCalled();
    });

    it('calls store.toggleActive DIRECTLY (no confirm dialog) when ACTIVATING a staff member', () => {
      component.toggleActive('s1', false);
      expect(mockConfirmSvc.confirm).not.toHaveBeenCalled();
      expect(mockStaffStore.toggleActive).toHaveBeenCalledWith('s1', false);
    });

    it('shows error toast when toggle fails', () => {
      mockStaffStore.toggleActive.mockReturnValue(throwError(() => new Error('Network')));
      component.toggleActive('s1', false); // activation path (no confirm)
      expect(mockToastSvc.error).toHaveBeenCalledWith('staff.statusUpdateFailed');
    });
  });

  // ── Reset password ────────────────────────────────────────────────────────────

  describe('openResetPassword() / saveResetPassword()', () => {
    it('openResetPassword() stores the target staff id and name', () => {
      component.openResetPassword(makeStaff({ id: 'rp-1', fullName: 'Jane Doe' }));
      expect(component.resetPassTarget()).toEqual({ id: 'rp-1', name: 'Jane Doe' });
    });

    it('closeResetPassword() clears the resetPassTarget', () => {
      component.openResetPassword(makeStaff());
      component.closeResetPassword();
      expect(component.resetPassTarget()).toBeNull();
    });

    it('saveResetPassword() does nothing when password is shorter than 8 chars', () => {
      component.openResetPassword(makeStaff({ id: 'rp-1' }));
      component.newPassword = 'short';
      component.saveResetPassword();
      expect(mockStaffStore.resetPassword).not.toHaveBeenCalled();
    });

    it('saveResetPassword() calls store.resetPassword with target id and new password', () => {
      component.openResetPassword(makeStaff({ id: 'rp-1' }));
      component.newPassword = 'NewSecureP@ss1';
      component.saveResetPassword();
      expect(mockStaffStore.resetPassword).toHaveBeenCalledWith('rp-1', 'NewSecureP@ss1');
    });

    it('shows success toast and closes modal after reset', () => {
      component.openResetPassword(makeStaff({ id: 'rp-1', fullName: 'Jane Doe' }));
      component.newPassword = 'NewSecureP@ss1';
      component.saveResetPassword();
      expect(mockToastSvc.success).toHaveBeenCalled();
      expect(component.resetPassTarget()).toBeNull();
    });

    it('shows error toast when reset API fails', () => {
      component.openResetPassword(makeStaff({ id: 'rp-1' }));
      component.newPassword = 'NewSecureP@ss1';
      mockStaffStore.resetPassword.mockReturnValue(
        throwError(() => ({ error: { error: 'Reset failed.' } })),
      );
      component.saveResetPassword();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Reset failed.');
    });

    it('clears savingReset flag after reset error', () => {
      component.openResetPassword(makeStaff());
      component.newPassword = 'NewSecureP@ss1';
      mockStaffStore.resetPassword.mockReturnValue(throwError(() => ({})));
      component.saveResetPassword();
      expect(component.savingReset()).toBe(false);
    });
  });

  // ── Bulk actions ──────────────────────────────────────────────────────────────

  describe('bulk selection', () => {
    it('toggleBulkMode() enters bulk mode and clears selection on exit', () => {
      component.toggleBulkMode();
      expect(component.bulkSelecting()).toBe(true);
      component.selectedIds.set(new Set(['a', 'b']));
      component.toggleBulkMode();
      expect(component.bulkSelecting()).toBe(false);
      expect(component.selectedCount()).toBe(0);
    });

    it('toggleSelect() adds then removes an id', () => {
      component.toggleSelect('s1');
      expect(component.selectedIds().has('s1')).toBe(true);
      component.toggleSelect('s1');
      expect(component.selectedIds().has('s1')).toBe(false);
    });

    it('allVisibleSelected() is true only when every row on the page is selected', () => {
      mockStaffStore.staff.mockReturnValue([makeStaff({ id: 'a' }), makeStaff({ id: 'b' })]);
      const c = TestBed.createComponent(StaffComponent).componentInstance;
      expect(c.allVisibleSelected()).toBe(false);
      c.selectedIds.set(new Set(['a', 'b']));
      expect(c.allVisibleSelected()).toBe(true);
    });
  });

  describe('bulk actions', () => {
    beforeEach(() => component.selectedIds.set(new Set(['a', 'b'])));

    it('saveBulkLocations() calls the store with selected ids + chosen locations', () => {
      component.bulkLocations = ['loc1'];
      component.saveBulkLocations();
      expect(mockStaffStore.bulkAssignLocations).toHaveBeenCalledWith(['a', 'b'], ['loc1']);
      expect(mockToastSvc.success).toHaveBeenCalled();
      expect(component.selectedCount()).toBe(0);   // cleared after success
    });

    it('saveBulkRoles() calls the store with selected ids + chosen roles', () => {
      component.bulkRoles = ['BARTENDER'];
      component.saveBulkRoles();
      expect(mockStaffStore.bulkAssignRoles).toHaveBeenCalledWith(['a', 'b'], ['BARTENDER']);
    });

    it('saveBulkLocations() does nothing when no locations are chosen', () => {
      component.bulkLocations = [];
      component.saveBulkLocations();
      expect(mockStaffStore.bulkAssignLocations).not.toHaveBeenCalled();
    });

    it('bulkDeactivateSelected() confirms then calls the store', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.bulkDeactivateSelected();
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
      expect(mockStaffStore.bulkDeactivate).toHaveBeenCalledWith(['a', 'b']);
    });

    it('bulkDeactivateSelected() does NOT call the store when cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.bulkDeactivateSelected();
      expect(mockStaffStore.bulkDeactivate).not.toHaveBeenCalled();
    });

    it('surfaces the API error (e.g. last-admin guard) as a toast', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockStaffStore.bulkDeactivate.mockReturnValue(
        throwError(() => ({ error: { error: 'Keep at least one administrator active.' } })),
      );
      component.bulkDeactivateSelected();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Keep at least one administrator active.');
    });
  });

  // ── totalPages computed ───────────────────────────────────────────────────────

  describe('totalPages computed', () => {
    it('returns at least 1 even when there are no staff records', () => {
      mockStaffStore.totalCount.mockReturnValue(0);
      expect(component.totalPages()).toBe(1);
    });

    it('calculates correct page count (ceiling division)', () => {
      mockStaffStore.totalCount.mockReturnValue(25);
      // totalCount is a plain fn mock (not a signal), so re-creating a fresh computed is needed
      // Instead, verify via a fresh component instance
      const c2 = TestBed.createComponent(StaffComponent).componentInstance;
      mockStaffStore.totalCount.mockReturnValue(25);
      // 25 / 10 = 2.5 → ceil = 3
      expect(c2.totalPages()).toBe(3);
    });
  });
});
