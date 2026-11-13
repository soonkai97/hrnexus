import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { RolesComponent } from './roles.component';
import { StaffStore, ToastService, ConfirmService, PermissionAdminService } from 'shared-lib';

/**
 * RolesComponent — job-role CRUD with inline rate editing.
 *
 * WHY these tests matter:
 *  - addRole() must trim and guard empty names — a blank role name breaks the
 *    role dropdown in every staff form and can cause FK constraint errors.
 *  - saveRate() must clear editingRateName after success — leaving it set would
 *    keep the row in edit mode, allowing accidental double-saves.
 *  - deleteRole() confirm flow ensures managers don't accidentally remove a role
 *    that is still assigned to staff members.
 */

describe('RolesComponent', () => {
  let component: RolesComponent;

  let mockStaffStore: {
    loadRoles:      ReturnType<typeof vi.fn>;
    roles:          ReturnType<typeof vi.fn>;
    loading:        ReturnType<typeof vi.fn>;
    createRole:     ReturnType<typeof vi.fn>;
    updateRoleRate: ReturnType<typeof vi.fn>;
    deleteRole:     ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };
  let mockPermSvc: {
    catalog:            ReturnType<typeof vi.fn>;
    getRolePermissions: ReturnType<typeof vi.fn>;
    setRolePermissions: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    mockStaffStore = {
      loadRoles:      vi.fn(),
      roles:          vi.fn().mockReturnValue([]),
      loading:        vi.fn().mockReturnValue(false),
      createRole:     vi.fn().mockReturnValue(of(undefined)),
      updateRoleRate: vi.fn().mockReturnValue(of(undefined)),
      deleteRole:     vi.fn().mockReturnValue(of(undefined)),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };
    mockPermSvc    = {
      catalog:            vi.fn().mockReturnValue(of(['portal.admin', 'staff.view'])),
      getRolePermissions: vi.fn().mockReturnValue(of(['staff.view'])),
      setRolePermissions: vi.fn().mockReturnValue(of(undefined)),
    };

    await TestBed.configureTestingModule({
      imports: [RolesComponent],
      providers: [
        provideHttpClient(withXhr()),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: StaffStore,    useValue: mockStaffStore },
        { provide: ToastService,  useValue: mockToastSvc },
        { provide: ConfirmService, useValue: mockConfirmSvc },
        { provide: PermissionAdminService, useValue: mockPermSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(RolesComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  it('loads roles on init so the table is populated', () => {
    component.ngOnInit();
    expect(mockStaffStore.loadRoles).toHaveBeenCalled();
  });

  // ── addRole() ─────────────────────────────────────────────────────────────────

  describe('addRole()', () => {
    it('does nothing when newRoleName is empty — blank role names are invalid', () => {
      component.newRoleName = '';
      component.addRole();
      expect(mockStaffStore.createRole).not.toHaveBeenCalled();
    });

    it('does nothing when newRoleName is only whitespace', () => {
      component.newRoleName = '   ';
      component.addRole();
      expect(mockStaffStore.createRole).not.toHaveBeenCalled();
    });

    it('calls createRole with the trimmed uppercased name and rate', () => {
      component.newRoleName = '  bartender  ';
      component.newRoleRate = 15;
      component.addRole();
      expect(mockStaffStore.createRole).toHaveBeenCalledWith('BARTENDER', 15, null, null, 'hourly');
    });

    it('passes the selected pay type (fixed) through to createRole', () => {
      component.newRoleName = 'package';
      component.newRoleRate = 500;
      component.newRolePayType = 'fixed';
      component.addRole();
      expect(mockStaffStore.createRole).toHaveBeenCalledWith('PACKAGE', 500, null, null, 'fixed');
    });

    it('shows success toast after creating a role', () => {
      component.newRoleName = 'host';
      component.addRole();
      expect(mockToastSvc.success).toHaveBeenCalledWith('roles.created');
    });

    it('hides the form after successfully creating a role', () => {
      component.newRoleName = 'host';
      component.showForm.set(true);
      component.addRole();
      expect(component.showForm()).toBe(false);
    });

    it('resets newRoleName after success', () => {
      component.newRoleName = 'host';
      component.addRole();
      expect(component.newRoleName).toBe('');
    });

    it('shows error toast on API failure', () => {
      mockStaffStore.createRole.mockReturnValue(throwError(() => ({ error: { error: 'Role exists.' } })));
      component.newRoleName = 'bartender';
      component.addRole();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Role exists.');
    });

    it('clears saving flag on API failure', () => {
      mockStaffStore.createRole.mockReturnValue(throwError(() => new Error('Network')));
      component.newRoleName = 'host';
      component.addRole();
      expect(component.saving()).toBe(false);
    });
  });

  // ── startEditRate() / cancelEditRate() ────────────────────────────────────────

  const bartender = { id: 'role-bartender-id', name: 'BARTENDER', hourlyRate: 12, nameZh: null, nameVi: null, payType: 'hourly' as const, isSystem: false };

  describe('startEditRate() / cancelEditRate()', () => {
    it('sets editingRateId to the role id', () => {
      component.startEditRate(bartender);
      expect(component.editingRateId()).toBe('role-bartender-id');
    });

    it('seeds the English name so the modal is editable for custom jobs', () => {
      component.startEditRate({ ...bartender, name: 'MIXOLOGIST' });
      expect(component.editingRateName()).toBe('MIXOLOGIST');
      expect(component.editingRateIsSystem()).toBe(false);
    });

    it('sets editingRateValue to the current rate', () => {
      component.startEditRate({ ...bartender, hourlyRate: 12.50 });
      expect(component.editingRateValue()).toBe(12.50);
    });

    it('cancelEditRate clears editingRateId', () => {
      component.startEditRate(bartender);
      component.cancelEditRate();
      expect(component.editingRateId()).toBeNull();
    });

    it('seeds editingRatePayType from the role so the toggle reflects the current pay model', () => {
      component.startEditRate({ id: 'role-package-id', name: 'PACKAGE', hourlyRate: 500, nameZh: null, nameVi: null, payType: 'fixed', isSystem: false });
      expect(component.editingRatePayType()).toBe('fixed');
    });

    it('marks system roles so the English-name field is locked', () => {
      component.startEditRate({ id: 'role-admin-id', name: 'SUPERADMIN', hourlyRate: 0, nameZh: null, nameVi: null, payType: 'hourly', isSystem: true });
      expect(component.editingRateIsSystem()).toBe(true);
    });
  });

  // ── saveRate() ────────────────────────────────────────────────────────────────

  describe('saveRate()', () => {
    beforeEach(() => {
      component.editingRateId.set('role-bartender-id');
      component.editingRateName.set('BARTENDER');
      component.editingRateValue.set(15);
    });

    it('calls updateRoleRate with the role id, new rate, and English name (custom job)', () => {
      component.saveRate();
      expect(mockStaffStore.updateRoleRate).toHaveBeenCalledWith('role-bartender-id', 15, null, null, 'hourly', 'BARTENDER');
    });

    it('sends a null name for system roles so their auth key is never renamed', () => {
      component.editingRateIsSystem.set(true);
      component.editingRateName.set('SUPERADMIN');
      component.saveRate();
      expect(mockStaffStore.updateRoleRate).toHaveBeenCalledWith('role-bartender-id', 15, null, null, 'hourly', null);
    });

    it('does nothing when a custom job has a blank English name', () => {
      component.editingRateName.set('   ');
      component.saveRate();
      expect(mockStaffStore.updateRoleRate).not.toHaveBeenCalled();
    });

    it('clears editingRateId after success — prevents accidental double-save', () => {
      component.saveRate();
      expect(component.editingRateId()).toBeNull();
    });

    it('shows success toast after updating the rate', () => {
      component.saveRate();
      expect(mockToastSvc.success).toHaveBeenCalledWith('roles.rateUpdated');
    });

    it('clears savingRate flag after success', () => {
      component.saveRate();
      expect(component.savingRate()).toBe(false);
    });

    it('shows error toast on API failure', () => {
      mockStaffStore.updateRoleRate.mockReturnValue(throwError(() => ({ error: { error: 'Not found.' } })));
      component.saveRate();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Not found.');
    });

    it('clears savingRate flag on API failure', () => {
      mockStaffStore.updateRoleRate.mockReturnValue(throwError(() => new Error('Network')));
      component.saveRate();
      expect(component.savingRate()).toBe(false);
    });

    it('does nothing when editingRateId is null', () => {
      component.editingRateId.set(null);
      component.saveRate();
      expect(mockStaffStore.updateRoleRate).not.toHaveBeenCalled();
    });
  });

  // ── deleteRole() ──────────────────────────────────────────────────────────────

  describe('deleteRole()', () => {
    it('shows confirmation dialog before deleting', () => {
      component.deleteRole('role-bartender-id');
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls store.deleteRole with the role id when confirmed', () => {
      component.deleteRole('role-bartender-id');
      expect(mockStaffStore.deleteRole).toHaveBeenCalledWith('role-bartender-id');
    });

    it('shows success toast after deletion', () => {
      component.deleteRole('role-bartender-id');
      expect(mockToastSvc.success).toHaveBeenCalledWith('roles.deleted');
    });

    it('does NOT delete when confirmation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.deleteRole('role-bartender-id');
      expect(mockStaffStore.deleteRole).not.toHaveBeenCalled();
    });

    it('shows error toast when deletion fails', () => {
      mockStaffStore.deleteRole.mockReturnValue(throwError(() => ({ error: { error: 'Role in use.' } })));
      component.deleteRole('role-bartender-id');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Role in use.');
    });
  });

  // ── Role permissions editor ─────────────────────────────────────────────────

  describe('openPermissions()', () => {
    it('opens the dialog for the given role', () => {
      component.openPermissions('BARTENDER');
      expect(component.permRole()).toBe('BARTENDER');
    });

    it('loads the catalog and pre-checks the role\'s current permissions', () => {
      component.openPermissions('BARTENDER');
      expect(component.permCatalog()).toEqual(['portal.admin', 'staff.view']);
      expect(component.permSelected()).toEqual(['staff.view']);
    });

    it('only fetches the catalog once across opens', () => {
      component.openPermissions('BARTENDER');
      component.openPermissions('HOST');
      expect(mockPermSvc.catalog).toHaveBeenCalledTimes(1);
    });

    it('toasts loadFailed when current permissions cannot be loaded', () => {
      mockPermSvc.getRolePermissions.mockReturnValue(throwError(() => new Error('boom')));
      component.openPermissions('BARTENDER');
      expect(mockToastSvc.error).toHaveBeenCalledWith('permissions.loadFailed');
    });
  });

  describe('togglePermission()', () => {
    it('adds a key when checked', () => {
      component.permSelected.set([]);
      component.togglePermission('staff.view', true);
      expect(component.permSelected()).toEqual(['staff.view']);
    });

    it('removes a key when unchecked', () => {
      component.permSelected.set(['staff.view', 'portal.admin']);
      component.togglePermission('staff.view', false);
      expect(component.permSelected()).toEqual(['portal.admin']);
    });
  });

  describe('savePermissions()', () => {
    beforeEach(() => {
      component.permRole.set('BARTENDER');
      component.permSelected.set(['staff.view']);
    });

    it('persists the selected keys for the open role', () => {
      component.savePermissions();
      expect(mockPermSvc.setRolePermissions).toHaveBeenCalledWith('BARTENDER', ['staff.view']);
    });

    it('toasts success and closes the dialog after saving', () => {
      component.savePermissions();
      expect(mockToastSvc.success).toHaveBeenCalledWith('permissions.saved');
      expect(component.permRole()).toBeNull();
    });

    it('toasts saveFailed and keeps the dialog open on error', () => {
      mockPermSvc.setRolePermissions.mockReturnValue(throwError(() => new Error('boom')));
      component.savePermissions();
      expect(mockToastSvc.error).toHaveBeenCalledWith('permissions.saveFailed');
      expect(component.permRole()).toBe('BARTENDER');
      expect(component.permSaving()).toBe(false);
    });

    it('does nothing when no role is open', () => {
      component.permRole.set(null);
      component.savePermissions();
      expect(mockPermSvc.setRolePermissions).not.toHaveBeenCalled();
    });
  });
});
