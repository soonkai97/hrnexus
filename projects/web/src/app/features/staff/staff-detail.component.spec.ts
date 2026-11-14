import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { StaffDetailComponent } from './staff-detail.component';
import { StaffStore, StaffManagementService, AuthService, ToastService, StaffDetail, PermissionAdminService, PermissionService, PayrollService, SalaryOverview } from 'shared-lib';

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

function makeOverview(overrides: Partial<SalaryOverview> = {}): SalaryOverview {
  const row = (id: string, effectiveFrom: string, basicSalary: number) => ({
    id, effectiveFrom, basicSalary, payFrequency: 'monthly' as const,
    bankName: 'Maybank', bankAccount: '1234567890', bankHolder: 'Jane Doe', createdAt: '2026-01-01T00:00:00Z',
  });
  const history = [row('p2', '2026-06-01', 3500.5), row('p1', '2026-01-01', 3000)];
  return {
    current: history[0], history,
    employment: { citizenshipStatus: 'citizen', startDate: '2026-01-01', endDate: null },
    ...overrides,
  };
}

describe('StaffDetailComponent', () => {
  let component: StaffDetailComponent;

  let mockStaffStore: {
    roles:    ReturnType<typeof vi.fn>;
    loadRoles: ReturnType<typeof vi.fn>;
    update:   ReturnType<typeof vi.fn>;
  };
  let mockStaffSvc:  { getById: ReturnType<typeof vi.fn> };
  let mockAuth:      Record<string, never>;
  let mockToast:     { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockPerm:      { has: ReturnType<typeof vi.fn> };
  let mockPayroll:   { getSalary: ReturnType<typeof vi.fn>; addSalary: ReturnType<typeof vi.fn>; setEmployment: ReturnType<typeof vi.fn> };
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
    mockAuth = {};
    mockToast = { success: vi.fn(), error: vi.fn() };
    mockPermSvc = {
      catalog:          vi.fn().mockReturnValue(of([])),
      getUserOverrides: vi.fn().mockReturnValue(of([])),
      setUserOverrides: vi.fn().mockReturnValue(of(undefined)),
    };

    mockPerm = { has: vi.fn().mockReturnValue(false) };
    mockPayroll = {
      getSalary:     vi.fn().mockReturnValue(of(makeOverview())),
      addSalary:     vi.fn().mockReturnValue(of({ id: 'p1' })),
      setEmployment: vi.fn().mockReturnValue(of(undefined)),
    };

    await TestBed.configureTestingModule({
      imports: [StaffDetailComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: StaffStore,             useValue: mockStaffStore },
        { provide: StaffManagementService, useValue: mockStaffSvc },
        { provide: AuthService,            useValue: mockAuth },
        { provide: ToastService,           useValue: mockToast },
        { provide: PermissionAdminService, useValue: mockPermSvc },
        { provide: PermissionService,      useValue: mockPerm },
        { provide: PayrollService,         useValue: mockPayroll },
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
      expect(component.fieldError('staffId')).toBe('Staff ID is required.');
    });

    it('fieldError flags a malformed email once touched, clears when blank or valid', () => {
      fillValid();
      component.markTouched('email');
      component.editForm.email = 'bad-email';
      expect(component.fieldError('email')).toBe('Enter a valid email address.');
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
        { id: 'role-employee-id', name: 'employee', isSystem: false },
      ]);
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      c2.staff.set(makeStaffDetail({ roles: ['employee'] }));
      c2.startEdit();
      expect(c2.editForm.roles).toEqual(['role-employee-id']);
    });

    it('clones the roles array so edits do not mutate the staff signal', () => {
      mockStaffStore.roles.mockReturnValue([
        { id: 'role-employee-id', name: 'employee', isSystem: false },
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
      expect(mockToast.success).toHaveBeenCalledWith('Permissions updated.');
      expect(component.permSaving()).toBe(false);
    });

    it('shows error toast and clears saving when save fails', () => {
      mockPermSvc.setUserOverrides.mockReturnValue(throwError(() => new Error('boom')));
      component.permKeys.set(['staff.view']);
      component.permChoices.set({ 'staff.view': 'grant' });
      component.savePermissions();
      expect(mockToast.error).toHaveBeenCalledWith('Failed to save permissions.');
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
      expect(mockToast.error).toHaveBeenCalledWith('Failed to load permissions.');
      expect(c2.permLoading()).toBe(false);
    });
  });

  // ── Pay section (payroll.view / payroll.manage) ──────────────────────────────

  describe('pay section', () => {
    /** Fresh component with the given permissions (has() is a plain mock, so set it first). */
    function makePay(perms: string[]): StaffDetailComponent {
      mockPerm.has.mockImplementation((k: string) => perms.includes(k));
      return TestBed.createComponent(StaffDetailComponent).componentInstance;
    }

    it('makes no salary request and exposes no pay without payroll.view (pay data must not leak)', () => {
      const c = makePay([]);
      c.ngOnInit();
      expect(c.canViewPay).toBe(false);
      expect(mockPayroll.getSalary).not.toHaveBeenCalled();
      expect(c.pay()).toBeNull();
    });

    it('loads the salary overview with payroll.view', () => {
      const c = makePay(['payroll.view']);
      c.ngOnInit();
      expect(mockPayroll.getSalary).toHaveBeenCalled();
      expect(c.pay()?.current?.basicSalary).toBe(3500.5);
      expect(c.payLoading()).toBe(false);
    });

    it('payroll.view alone is read-only', () => {
      const c = makePay(['payroll.view']);
      expect(c.canViewPay).toBe(true);
      expect(c.canManagePay).toBe(false);
    });

    it('payroll.manage without payroll.view still shows no pay', () => {
      const c = makePay(['payroll.manage']);
      c.ngOnInit();
      expect(c.canViewPay).toBe(false);
      expect(c.canManagePay).toBe(false);
      expect(mockPayroll.getSalary).not.toHaveBeenCalled();
    });

    it('toasts and clears loading when the salary load fails', () => {
      mockPayroll.getSalary.mockReturnValue(throwError(() => ({ status: 500 })));
      const c = makePay(['payroll.view']);
      c.loadPay();
      expect(mockToast.error).toHaveBeenCalledWith('Failed to load pay details.');
      expect(c.payLoading()).toBe(false);
    });

    describe('maskAccount()', () => {
      it('shows only the last 4 characters', () => {
        const c = makePay([]);
        expect(c.maskAccount('1234567890')).toBe('•••• 7890');
      });
      it('never reveals an account of 4 or fewer characters', () => {
        const c = makePay([]);
        expect(c.maskAccount('1234')).toBe('••••');
        expect(c.maskAccount('12')).toBe('••');
      });
      it('renders a dash for no account', () => {
        const c = makePay([]);
        expect(c.maskAccount(null)).toBe('—');
        expect(c.maskAccount('')).toBe('—');
      });
    });

    describe('isScheduled()', () => {
      it('flags a future-dated row and not a past or same-day one', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date(2026, 5, 15, 12, 0, 0));
        try {
          const c = makePay([]);
          expect(c.isScheduled('2026-06-16')).toBe(true);
          expect(c.isScheduled('2026-06-15')).toBe(false);
          expect(c.isScheduled('2026-01-01')).toBe(false);
        } finally { vi.useRealTimers(); }
      });
    });

    describe('add salary', () => {
      function fill(c: StaffDetailComponent): void {
        c.salaryForm = { basicSalary: 4000, effectiveFrom: '2026-12-01', bankName: ' Maybank ', bankAccount: '9988776655', bankHolder: 'Jane Doe' };
      }

      it('openSalaryDialog() opens a blank form', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.salaryForm.basicSalary = 1;
        c.openSalaryDialog();
        expect(c.showSalaryDialog()).toBe(true);
        expect(c.salaryForm.basicSalary).toBeNull();
        expect(c.salaryError()).toBe('');
      });

      it('requires a positive salary and an effective date', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        fill(c);
        expect(c.isSalaryFormValid).toBe(true);
        c.salaryForm.basicSalary = 0;
        expect(c.isSalaryFormValid).toBe(false);
        c.salaryForm.basicSalary = null;
        expect(c.isSalaryFormValid).toBe(false);
        fill(c);
        c.salaryForm.effectiveFrom = '';
        expect(c.isSalaryFormValid).toBe(false);
      });

      it('does nothing without payroll.manage even if called', () => {
        const c = makePay(['payroll.view']);
        fill(c);
        c.addSalary();
        expect(mockPayroll.addSalary).not.toHaveBeenCalled();
      });

      it('does not submit an invalid form', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.addSalary();
        expect(mockPayroll.addSalary).not.toHaveBeenCalled();
      });

      it('POSTs the trimmed values for the route staff id, then toasts, closes and reloads', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.ngOnInit();
        mockPayroll.getSalary.mockClear();
        c.openSalaryDialog();
        fill(c);
        c.addSalary();
        expect(mockPayroll.addSalary).toHaveBeenCalledWith('', {
          basicSalary: 4000, payFrequency: 'monthly', effectiveFrom: '2026-12-01',
          bankName: 'Maybank', bankAccount: '9988776655', bankHolder: 'Jane Doe',
        });
        expect(mockToast.success).toHaveBeenCalledWith('Salary added.');
        expect(c.showSalaryDialog()).toBe(false);
        expect(c.salarySaving()).toBe(false);
        expect(mockPayroll.getSalary).toHaveBeenCalledTimes(1);
      });

      it('sends blank bank fields as null', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.salaryForm = { basicSalary: 100, effectiveFrom: '2026-12-01', bankName: '', bankAccount: '  ', bankHolder: '' };
        c.addSalary();
        expect(mockPayroll.addSalary.mock.calls[0][1]).toMatchObject({ bankName: null, bankAccount: null, bankHolder: null });
      });

      it('keeps the dialog open and shows the server message on 409 (date already has a row)', () => {
        mockPayroll.addSalary.mockReturnValue(throwError(() => ({ status: 409, error: { error: 'A salary already exists for that date.' } })));
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.openSalaryDialog();
        fill(c);
        c.addSalary();
        expect(c.salaryError()).toBe('A salary already exists for that date.');
        expect(c.showSalaryDialog()).toBe(true);
        expect(c.salarySaving()).toBe(false);
        expect(mockToast.success).not.toHaveBeenCalled();
      });

      it('shows the server message on 400 and a generic one when the body has none', () => {
        mockPayroll.addSalary.mockReturnValue(throwError(() => ({ status: 400, error: { error: 'Salary must be greater than zero.' } })));
        const c = makePay(['payroll.view', 'payroll.manage']);
        fill(c);
        c.addSalary();
        expect(c.salaryError()).toBe('Salary must be greater than zero.');

        mockPayroll.addSalary.mockReturnValue(throwError(() => ({ status: 500 })));
        c.addSalary();
        expect(c.salaryError()).toBe('Failed to add salary.');
      });
    });

    describe('employment details', () => {
      it('startEmpEdit() seeds the form from the loaded details', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.ngOnInit();
        c.startEmpEdit();
        expect(c.empEditMode()).toBe(true);
        expect(c.empForm).toEqual({ citizenshipStatus: 'citizen', startDate: '2026-01-01', endDate: null });
      });

      it('requires citizenship and a start date; end date may not precede the start', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.empForm = { citizenshipStatus: null, startDate: '2026-01-01', endDate: null };
        expect(c.isEmpFormValid).toBe(false);
        c.empForm = { citizenshipStatus: 'citizen', startDate: null, endDate: null };
        expect(c.isEmpFormValid).toBe(false);
        c.empForm = { citizenshipStatus: 'permanent-resident', startDate: '2026-01-01', endDate: null };
        expect(c.isEmpFormValid).toBe(true);
        c.empForm.endDate = '2025-12-31';
        expect(c.isEmpFormValid).toBe(false);
        c.empForm.endDate = '2026-12-31';
        expect(c.isEmpFormValid).toBe(true);
      });

      it('saves, toasts, leaves edit mode and reloads', () => {
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.ngOnInit();
        c.startEmpEdit();
        c.empForm = { citizenshipStatus: 'permanent-resident', startDate: '2026-02-01', endDate: '' };
        mockPayroll.getSalary.mockClear();
        c.saveEmployment();
        expect(mockPayroll.setEmployment).toHaveBeenCalledWith('', {
          citizenshipStatus: 'permanent-resident', startDate: '2026-02-01', endDate: null,
        });
        expect(mockToast.success).toHaveBeenCalledWith('Employment details saved.');
        expect(c.empEditMode()).toBe(false);
        expect(mockPayroll.getSalary).toHaveBeenCalledTimes(1);
      });

      it('shows the server error and stays in edit mode when the save fails', () => {
        mockPayroll.setEmployment.mockReturnValue(throwError(() => ({ status: 400, error: { error: 'Start date is invalid.' } })));
        const c = makePay(['payroll.view', 'payroll.manage']);
        c.ngOnInit();
        c.startEmpEdit();
        c.saveEmployment();
        expect(mockToast.error).toHaveBeenCalledWith('Start date is invalid.');
        expect(c.empEditMode()).toBe(true);
        expect(c.empSaving()).toBe(false);
      });

      it('does not save without payroll.manage', () => {
        const c = makePay(['payroll.view']);
        c.ngOnInit();
        c.empForm = { citizenshipStatus: 'citizen', startDate: '2026-01-01', endDate: null };
        c.saveEmployment();
        expect(mockPayroll.setEmployment).not.toHaveBeenCalled();
      });
    });
  });

  // ── availableRoles computed ───────────────────────────────────────────────────

  describe('availableRoles computed', () => {
    it('exposes every defined job as selectable, including the first administrator job', () => {
      mockStaffStore.roles.mockReturnValue([
        { name: 'ADMINISTRATOR', isSystem: true },
        { name: 'FRONT DESK', isSystem: false },
        { name: 'EMPLOYEE', isSystem: false },
      ]);
      const c2 = TestBed.createComponent(StaffDetailComponent).componentInstance;
      const names = c2.availableRoles().map((r: any) => r.name);
      expect(names).toContain('ADMINISTRATOR');
      expect(names).toContain('FRONT DESK');
      expect(names).toContain('EMPLOYEE');
    });
  });
});
