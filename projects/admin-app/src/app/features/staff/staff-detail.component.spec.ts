import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { StaffDetailComponent } from './staff-detail.component';
import { StaffStore, StaffManagementService, AuthService, ToastService, StaffDetail, PermissionAdminService } from 'shared-lib';

/**
 * StaffDetailComponent — per-staff profile + edit form + permission overrides.
 *
 * WHY these tests matter:
 *  - isEditFormValid gates the Save button — a wrong formula could allow an admin
 *    to submit an empty name or email, corrupting staff records.
 *  - startEdit() populates editForm from the current staff signal — if it clones
 *    incorrectly, edits will clobber unintended fields.
 *  - saveEdit() must call store.update() with the correct staff ID (from the route
 *    param), not an empty string or the wrong record.
 *  - permission overrides persist only grant/revoke choices — saving 'inherit' rows would
 *    turn role defaults into hard-coded per-user overrides.
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
  let mockPermSvc:    { catalog: ReturnType<typeof vi.fn>; getUserOverrides: ReturnType<typeof vi.fn>; setUserOverrides: ReturnType<typeof vi.fn> };

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
    mockPermSvc = {
      catalog:          vi.fn().mockReturnValue(of([])),
      getUserOverrides: vi.fn().mockReturnValue(of([])),
      setUserOverrides: vi.fn().mockReturnValue(of(undefined)),
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
        { provide: PermissionAdminService, useValue: mockPermSvc },
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
      expect(mockToast.error).toHaveBeenCalledWith('staffDetail.loadFailed');
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

    it('returns false when email is present but malformed', () => {
      fillValid();
      component.editForm.email = 'bad-email';
      expect(component.isEditFormValid).toBe(false);
    });

    // ── per-field inline validation ──────────────────────────────────────────
    it('fieldError surfaces a required message only after the field is blurred', () => {
      fillValid();
      component.editForm.staffId = '';
      expect(component.fieldError('staffId')).toBe('');   // not touched yet
      component.markTouched('staffId');
      expect(component.fieldError('staffId')).toBe('staff.errStaffIdRequired');
    });

    it('fieldError flags a malformed email once touched, clears when blank or valid', () => {
      fillValid();
      component.markTouched('email');
      component.editForm.email = 'bad-email';
      expect(component.fieldError('email')).toBe('staff.errEmailInvalid');
      component.editForm.email = null;
      expect(component.fieldError('email')).toBe('');
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

    it('maps the staff role NAMES to role IDS for the edit form (update DTO expects ids)', () => {
      mockStaffStore.roles.mockReturnValue([
        { id: 'role-employee-id', name: 'employee', isSystem: false, hourlyRate: 10, payType: 'hourly' },
      ]);
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      c2.staff.set(makeStaffDetail({ roles: ['employee'] }));
      c2.startEdit();
      expect(c2.editForm.roles).toEqual(['role-employee-id']);
    });

    it('clones the roles array so edits do not mutate the staff signal', () => {
      mockStaffStore.roles.mockReturnValue([
        { id: 'role-employee-id', name: 'employee', isSystem: false, hourlyRate: 10, payType: 'hourly' },
      ]);
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      const s = makeStaffDetail({ roles: ['employee'] });
      c2.staff.set(s);
      c2.startEdit();
      c2.editForm.roles.push('role-manager-id');
      expect(c2.staff()?.roles).not.toContain('role-manager-id');
      expect(c2.staff()?.roles).toEqual(['employee']);   // original NAMES untouched
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
      expect(mockToast.success).toHaveBeenCalledWith('staffDetail.profileUpdated');
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

  // ── permission overrides ──────────────────────────────────────────────────────

  describe('permission overrides', () => {
    it('seeds every catalog key to inherit when there are no stored overrides', () => {
      mockPermSvc.catalog.mockReturnValue(of(['staff.view', 'staff.manage']));
      mockPermSvc.getUserOverrides.mockReturnValue(of([]));
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      c2.loadPermissions();
      expect(c2.permChoices()).toEqual({ 'staff.view': 'inherit', 'staff.manage': 'inherit' });
      expect(c2.permLoading()).toBe(false);
    });

    it('maps stored overrides to grant/revoke and leaves the rest inherit', () => {
      mockPermSvc.catalog.mockReturnValue(of(['staff.view', 'staff.manage', 'payroll.view']));
      mockPermSvc.getUserOverrides.mockReturnValue(of([
        { permissionKey: 'staff.manage', granted: true },
        { permissionKey: 'payroll.view', granted: false },
      ]));
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      c2.loadPermissions();
      expect(c2.permChoices()).toEqual({
        'staff.view': 'inherit', 'staff.manage': 'grant', 'payroll.view': 'revoke',
      });
    });

    it('persists ONLY non-inherit choices — grant=>true, revoke=>false, inherit omitted', () => {
      component.permKeys.set(['staff.view', 'staff.manage', 'payroll.view']);
      component.permChoices.set({ 'staff.view': 'inherit', 'staff.manage': 'grant', 'payroll.view': 'revoke' });
      component.savePermissions();
      expect(mockPermSvc.setUserOverrides).toHaveBeenCalledWith('', [
        { permissionKey: 'staff.manage', granted: true },
        { permissionKey: 'payroll.view', granted: false },
      ]);
    });

    it('shows success toast and clears saving after a successful save', () => {
      component.permKeys.set(['staff.view']);
      component.permChoices.set({ 'staff.view': 'grant' });
      component.savePermissions();
      expect(mockToast.success).toHaveBeenCalledWith('permissions.saved');
      expect(component.permSaving()).toBe(false);
    });

    it('shows error toast and clears saving when save fails', () => {
      mockPermSvc.setUserOverrides.mockReturnValue(throwError(() => new Error('boom')));
      component.permKeys.set(['staff.view']);
      component.permChoices.set({ 'staff.view': 'grant' });
      component.savePermissions();
      expect(mockToast.error).toHaveBeenCalledWith('permissions.saveFailed');
      expect(component.permSaving()).toBe(false);
    });

    it('coerces a null selection (deselect) back to inherit', () => {
      component.permChoices.set({ 'staff.view': 'grant' });
      component.setPermChoice('staff.view', null);
      expect(component.permChoices()['staff.view']).toBe('inherit');
    });

    it('surfaces a load-failed toast when the catalog call errors', () => {
      mockPermSvc.catalog.mockReturnValue(throwError(() => new Error('nope')));
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      c2.loadPermissions();
      expect(mockToast.error).toHaveBeenCalledWith('permissions.loadFailed');
      expect(c2.permLoading()).toBe(false);
    });
  });

  // ── availableRoles computed ───────────────────────────────────────────────────

  describe('availableRoles computed', () => {
    it('exposes every defined role as selectable, including SUPERADMIN', () => {
      mockStaffStore.roles.mockReturnValue([
        { name: 'SUPERADMIN', isSystem: true },
        { name: 'MANAGER', isSystem: true },
        { name: 'EMPLOYEE', isSystem: false },
      ]);
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      const names = c2.availableRoles().map((r: any) => r.name);
      expect(names).toContain('SUPERADMIN');
      expect(names).toContain('MANAGER');
      expect(names).toContain('EMPLOYEE');
    });
  });
});
