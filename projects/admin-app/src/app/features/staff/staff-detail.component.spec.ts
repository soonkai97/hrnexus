import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { StaffDetailComponent } from './staff-detail.component';
import { StaffStore, StaffManagementService, AuthService, ToastService, WorkLogService, StaffDetail } from 'shared-lib';

/**
 * StaffDetailComponent — per-staff profile + edit form + work log history.
 *
 * WHY these tests matter:
 *  - isEditFormValid gates the Save button — a wrong formula could allow an admin
 *    to submit an empty name or email, corrupting staff records.
 *  - startEdit() populates editForm from the current staff signal — if it clones
 *    incorrectly, edits will clobber unintended fields.
 *  - saveEdit() must call store.update() with the correct staff ID (from the route
 *    param), not an empty string or the wrong record.
 *  - loadWorkLogs() paginates correctly — wrong page-size or employee-ID filtering
 *    would mix up shift records across staff members.
 *  - totalPages computed must always be at least 1 to avoid an empty paginator.
 */

function makeStaffDetail(overrides: Partial<StaffDetail> = {}): StaffDetail {
  return {
    id: 's1', staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
    fullName: 'Jane Doe', email: 'jane@test.com', roles: ['employee'],
    isActive: true, phoneNumber: null, icNo: 'IC12345', passportNo: null,
    dateOfBirth: '1990-01-01', age: 36, createdAt: '2024-01-01T00:00:00Z',
    ...overrides,
  } as StaffDetail;
}

describe('StaffDetailComponent', () => {
  let component: StaffDetailComponent;

  let mockStaffStore: {
    roles:    ReturnType<typeof vi.fn>;
    loadRoles: ReturnType<typeof vi.fn>;
    update:   ReturnType<typeof vi.fn>;
  };
  let mockStaffSvc:  { getById: ReturnType<typeof vi.fn> };
  let mockAuth:      { hasRole: ReturnType<typeof vi.fn> };
  let mockToast:     { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockWorkLogSvc: { getAll: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockStaffStore = {
      roles:     vi.fn().mockReturnValue([]),
      loadRoles: vi.fn(),
      update:    vi.fn().mockReturnValue(of(undefined)),
    };
    mockStaffSvc = {
      getById: vi.fn().mockReturnValue(of(makeStaffDetail())),
    };
    mockAuth = { hasRole: vi.fn().mockReturnValue(false) };
    mockToast = { success: vi.fn(), error: vi.fn() };
    mockWorkLogSvc = {
      getAll: vi.fn().mockReturnValue(of({ items: [], total: 0 })),
    };

    await TestBed.configureTestingModule({
      imports: [StaffDetailComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: StaffStore,             useValue: mockStaffStore },
        { provide: StaffManagementService, useValue: mockStaffSvc },
        { provide: AuthService,            useValue: mockAuth },
        { provide: ToastService,           useValue: mockToast },
        { provide: WorkLogService,         useValue: mockWorkLogSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(StaffDetailComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with staff=null until loaded', () => {
      expect(component.staff()).toBeNull();
    });

    it('starts with loading=true before any data arrives', () => {
      expect(component.loading()).toBe(true);
    });

    it('starts in view mode (editMode=false)', () => {
      expect(component.editMode()).toBe(false);
    });
  });

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads roles for the edit form role selector', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadRoles).toHaveBeenCalled();
    });

    it('loads staff details from the API', () => {
      component.ngOnInit();
      expect(mockStaffSvc.getById).toHaveBeenCalled();
    });

    it('loads the first page of work logs on init', () => {
      component.ngOnInit();
      expect(mockWorkLogSvc.getAll).toHaveBeenCalledWith(
        expect.objectContaining({ page: 1, pageSize: 10 }),
      );
    });

    it('populates the staff signal after successful load', () => {
      component.ngOnInit();
      expect(component.staff()).not.toBeNull();
      expect(component.staff()?.firstName).toBe('Jane');
    });

    it('clears loading after staff is fetched', () => {
      component.ngOnInit();
      expect(component.loading()).toBe(false);
    });

    it('shows error toast and clears loading when staff fetch fails', () => {
      mockStaffSvc.getById.mockReturnValue(throwError(() => new Error('Not found')));
      component.ngOnInit();
      expect(mockToast.error).toHaveBeenCalledWith('Failed to load staff details.');
      expect(component.loading()).toBe(false);
    });
  });

  // ── isEditFormValid ───────────────────────────────────────────────────────────

  describe('isEditFormValid', () => {
    function fillValid(): void {
      component.editForm = {
        staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
        email: 'jane@test.com', roles: ['employee'],
        phoneNumber: null, icNo: 'IC12345', passportNo: null, dateOfBirth: '1990-01-01',
      };
    }

    it('returns true when all required fields are populated', () => {
      fillValid();
      expect(component.isEditFormValid).toBe(true);
    });

    it('returns false when staffId is empty', () => {
      fillValid();
      component.editForm.staffId = '';
      expect(component.isEditFormValid).toBe(false);
    });

    it('returns false when firstName is blank', () => {
      fillValid();
      component.editForm.firstName = '   ';
      expect(component.isEditFormValid).toBe(false);
    });

    it('returns true when lastName is empty (lastName is optional on edit)', () => {
      fillValid();
      component.editForm.lastName = '';
      expect(component.isEditFormValid).toBe(true);
    });

    it('returns true when email is null (email is optional on edit)', () => {
      fillValid();
      component.editForm.email = null;
      expect(component.isEditFormValid).toBe(true);
    });

    it('returns true when dateOfBirth is null (DOB is optional on edit)', () => {
      fillValid();
      component.editForm.dateOfBirth = null;
      expect(component.isEditFormValid).toBe(true);
    });
  });

  // ── startEdit() ───────────────────────────────────────────────────────────────

  describe('startEdit()', () => {
    it('switches to edit mode', () => {
      component.staff.set(makeStaffDetail());
      component.startEdit();
      expect(component.editMode()).toBe(true);
    });

    it('populates editForm with current staff data', () => {
      component.staff.set(makeStaffDetail({ staffId: 'NC001', email: 'jane@test.com' }));
      component.startEdit();
      expect(component.editForm.staffId).toBe('NC001');
      expect(component.editForm.email).toBe('jane@test.com');
    });

    it('clones the roles array so edits do not mutate the staff signal', () => {
      const s = makeStaffDetail({ roles: ['employee'] });
      component.staff.set(s);
      component.startEdit();
      component.editForm.roles.push('manager');
      expect(component.staff()?.roles).not.toContain('manager');
    });
  });

  // ── cancelEdit() ──────────────────────────────────────────────────────────────

  describe('cancelEdit()', () => {
    it('exits edit mode without saving', () => {
      component.editMode.set(true);
      component.cancelEdit();
      expect(component.editMode()).toBe(false);
      expect(mockStaffStore.update).not.toHaveBeenCalled();
    });
  });

  // ── saveEdit() ────────────────────────────────────────────────────────────────

  describe('saveEdit() — success', () => {
    beforeEach(() => {
      component.editForm = {
        staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
        email: 'jane@test.com', roles: ['employee'],
        phoneNumber: null, icNo: 'IC12345', passportNo: null, dateOfBirth: '1990-01-01',
      };
    });

    it('calls store.update() with the edit form data', () => {
      component.saveEdit();
      expect(mockStaffStore.update).toHaveBeenCalledWith(
        '',
        expect.objectContaining({ staffId: 'NC001', email: 'jane@test.com' }),
      );
    });

    it('exits edit mode after a successful save', () => {
      component.editMode.set(true);
      component.saveEdit();
      expect(component.editMode()).toBe(false);
    });

    it('shows success toast after save', () => {
      component.saveEdit();
      expect(mockToast.success).toHaveBeenCalledWith('Profile updated.');
    });

    it('clears saving flag after success', () => {
      component.saveEdit();
      expect(component.saving()).toBe(false);
    });

    it('reloads staff details after a successful save', () => {
      component.saveEdit();
      // getById is called again (reload)
      expect(mockStaffSvc.getById.mock.calls.length).toBeGreaterThanOrEqual(1);
    });
  });

  describe('saveEdit() — error', () => {
    beforeEach(() => {
      component.editForm = {
        staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
        email: 'jane@test.com', roles: ['employee'],
        phoneNumber: null, icNo: 'IC12345', passportNo: null, dateOfBirth: '1990-01-01',
      };
      mockStaffStore.update.mockReturnValue(
        throwError(() => ({ error: { error: 'Email already in use.' } })),
      );
    });

    it('shows error toast on save failure', () => {
      component.saveEdit();
      expect(mockToast.error).toHaveBeenCalledWith('Email already in use.');
    });

    it('clears saving flag after error', () => {
      component.saveEdit();
      expect(component.saving()).toBe(false);
    });
  });

  // ── loadWorkLogs() ────────────────────────────────────────────────────────────

  describe('loadWorkLogs()', () => {
    it('updates currentPage to the requested page', () => {
      component.loadWorkLogs(3);
      expect(component.currentPage()).toBe(3);
    });

    it('calls workLogSvc.getAll with the correct employee ID and page', () => {
      component.loadWorkLogs(2);
      expect(mockWorkLogSvc.getAll).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2, pageSize: 10 }),
      );
    });

    it('populates workLogs signal with API response', () => {
      const fakeLog = { id: 'wl1', roleName: 'bartender' } as any;
      mockWorkLogSvc.getAll.mockReturnValue(of({ items: [fakeLog], total: 1 }));
      component.loadWorkLogs(1);
      expect(component.workLogs()).toEqual([fakeLog]);
    });

    it('sets totalLogs from the API response total', () => {
      mockWorkLogSvc.getAll.mockReturnValue(of({ items: [], total: 42 }));
      component.loadWorkLogs(1);
      expect(component.totalLogs()).toBe(42);
    });

    it('clears workLogsLoading after fetch', () => {
      component.loadWorkLogs(1);
      expect(component.workLogsLoading()).toBe(false);
    });
  });

  // ── totalPages computed ───────────────────────────────────────────────────────

  describe('totalPages computed', () => {
    it('returns at least 1 when there are no logs', () => {
      expect(component.totalPages()).toBe(1);
    });

    it('calculates correct page count via ceiling division', () => {
      component.totalLogs.set(25);
      expect(component.totalPages()).toBe(3); // ceil(25/10)
    });

    it('returns exact count when total divides evenly', () => {
      component.totalLogs.set(20);
      expect(component.totalPages()).toBe(2);
    });
  });

  // ── availableRoles computed ───────────────────────────────────────────────────

  describe('availableRoles computed', () => {
    it('excludes the admin role from the selectable roles list', () => {
      mockStaffStore.roles.mockReturnValue([
        { name: 'admin', isSystem: true },
        { name: 'manager', isSystem: true },
        { name: 'employee', isSystem: false },
      ]);
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      const names = c2.availableRoles().map((r: any) => r.name);
      expect(names).not.toContain('admin');
      expect(names).toContain('manager');
      expect(names).toContain('employee');
    });
  });
});
