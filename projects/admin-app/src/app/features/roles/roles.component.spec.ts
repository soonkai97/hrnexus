import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
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
    bulkCreateRoles: ReturnType<typeof vi.fn>;
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
      bulkCreateRoles: vi.fn().mockReturnValue(of({ created: 2, skipped: 0 })),
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
        provideHttpClient(),
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
      expect(mockStaffStore.createRole).toHaveBeenCalledWith('BARTENDER', 15, null, null);
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

  // ── saveBulk() — bulk add ──────────────────────────────────────────────────────

  describe('saveBulk()', () => {
    it('maps Name + Rate rows (trims, blank name dropped, null/negative rate → 0) and calls bulkCreateRoles', () => {
      component.bulkRows = [
        { name: ' Supervisor ', rate: 25 },
        { name: '', rate: 10 },          // blank name → dropped
        { name: 'Host', rate: null },    // null rate → 0
        { name: 'Security', rate: 22 },
      ];
      component.saveBulk();
      expect(mockStaffStore.bulkCreateRoles).toHaveBeenCalledWith([
        { name: 'Supervisor', hourlyRate: 25 },
        { name: 'Host', hourlyRate: 0 },
        { name: 'Security', hourlyRate: 22 },
      ]);
    });

    it('does nothing when no row has a name', () => {
      component.bulkRows = [{ name: '  ', rate: 0 }];
      component.saveBulk();
      expect(mockStaffStore.bulkCreateRoles).not.toHaveBeenCalled();
    });

    it('shows a created/skipped toast and closes the dialog on success', () => {
      component.bulkRows = [{ name: 'Host', rate: 18 }];
      component.showBulk.set(true);
      component.saveBulk();
      expect(mockToastSvc.success).toHaveBeenCalledWith('roles.bulkAddedToast');
      expect(component.showBulk()).toBe(false);
    });

    it('shows an error toast on failure', () => {
      mockStaffStore.bulkCreateRoles.mockReturnValue(throwError(() => ({ error: { error: 'Nope.' } })));
      component.bulkRows = [{ name: 'Host', rate: 18 }];
      component.saveBulk();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Nope.');
    });

    it('addBulkRow / removeBulkRow manage the row list', () => {
      component.bulkRows = [{ name: 'A', rate: 0 }];
      component.addBulkRow();
      expect(component.bulkRows.length).toBe(2);
      component.removeBulkRow(0);
      expect(component.bulkRows).toEqual([{ name: '', rate: 0 }]);
    });
  });

  // ── startEditRate() / cancelEditRate() ────────────────────────────────────────

  describe('startEditRate() / cancelEditRate()', () => {
    it('sets editingRateName to the role name', () => {
      component.startEditRate('bartender', 12);
      expect(component.editingRateName()).toBe('bartender');
    });

    it('sets editingRateValue to the current rate', () => {
      component.startEditRate('bartender', 12.50);
      expect(component.editingRateValue()).toBe(12.50);
    });

    it('cancelEditRate clears editingRateName', () => {
      component.startEditRate('bartender', 12);
      component.cancelEditRate();
      expect(component.editingRateName()).toBeNull();
    });
  });

  // ── saveRate() ────────────────────────────────────────────────────────────────

  describe('saveRate()', () => {
    beforeEach(() => {
      component.editingRateName.set('bartender');
      component.editingRateValue.set(15);
    });

    it('calls updateRoleRate with the role name and new rate', () => {
      component.saveRate();
      expect(mockStaffStore.updateRoleRate).toHaveBeenCalledWith('bartender', 15, null, null);
    });

    it('clears editingRateName after success — prevents accidental double-save', () => {
      component.saveRate();
      expect(component.editingRateName()).toBeNull();
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

    it('does nothing when editingRateName is null', () => {
      component.editingRateName.set(null);
      component.saveRate();
      expect(mockStaffStore.updateRoleRate).not.toHaveBeenCalled();
    });
  });

  // ── deleteRole() ──────────────────────────────────────────────────────────────

  describe('deleteRole()', () => {
    it('shows confirmation dialog before deleting', () => {
      component.deleteRole('bartender');
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls store.deleteRole when confirmed', () => {
      component.deleteRole('bartender');
      expect(mockStaffStore.deleteRole).toHaveBeenCalledWith('bartender');
    });

    it('shows success toast after deletion', () => {
      component.deleteRole('bartender');
      expect(mockToastSvc.success).toHaveBeenCalledWith('roles.deleted');
    });

    it('does NOT delete when confirmation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.deleteRole('bartender');
      expect(mockStaffStore.deleteRole).not.toHaveBeenCalled();
    });

    it('shows error toast when deletion fails', () => {
      mockStaffStore.deleteRole.mockReturnValue(throwError(() => ({ error: { error: 'Role in use.' } })));
      component.deleteRole('bartender');
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
