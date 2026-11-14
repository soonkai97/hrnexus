import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { RolesComponent } from './roles.component';
import { StaffStore, ToastService, ConfirmService, PermissionAdminService } from 'shared-lib';

/**
 * RolesComponent — role-role CRUD (add, rename custom roles, delete, permissions).
 *
 * WHY these tests matter:
 *  - addRole() must trim and guard empty names — a blank role name breaks the
 *    role dropdown in every staff form and can cause FK constraint errors.
 *  - saveEdit() must close the dialog after success — leaving it open would
 *    allow accidental double-saves.
 *  - System roles cannot be renamed — their name is the authorization key.
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
    renameRole:     ReturnType<typeof vi.fn>;
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
      renameRole:     vi.fn().mockReturnValue(of(undefined)),
      deleteRole:     vi.fn().mockReturnValue(of(undefined)),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };
    mockPermSvc    = {
      catalog:            vi.fn().mockReturnValue(of(['roles.manage', 'staff.view'])),
      getRolePermissions: vi.fn().mockReturnValue(of(['staff.view'])),
      setRolePermissions: vi.fn().mockReturnValue(of(undefined)),
    };

    await TestBed.configureTestingModule({
      imports: [RolesComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
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

    it('calls createRole with the trimmed uppercased name only — roles carry no pay data', () => {
      component.newRoleName = '  bartender  ';
      component.addRole();
      expect(mockStaffStore.createRole).toHaveBeenCalledWith('BARTENDER');
    });

    it('shows success toast after creating a role', () => {
      component.newRoleName = 'host';
      component.addRole();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Role created.');
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

  // ── startEdit() / cancelEdit() ────────────────────────────────────────────────

  const bartender = { id: 'role-bartender-id', name: 'BARTENDER', isSystem: false };

  describe('startEdit() / cancelEdit()', () => {
    it('opens the dialog for the role id, prefilled with its current name', () => {
      component.startEdit({ ...bartender, name: 'MIXOLOGIST' });
      expect(component.editingId()).toBe('role-bartender-id');
      expect(component.editingName()).toBe('MIXOLOGIST');
    });

    it('opens for the first administrator role too: nothing looks a role up by its name', () => {
      component.startEdit({ id: 'role-admin-id', name: 'ADMINISTRATOR', isSystem: true });
      expect(component.editingId()).toBe('role-admin-id');
      expect(component.editingName()).toBe('ADMINISTRATOR');
    });

    it('cancelEdit clears editingId', () => {
      component.startEdit(bartender);
      component.cancelEdit();
      expect(component.editingId()).toBeNull();
    });
  });

  // ── openRoleMenu() ────────────────────────────────────────────────────────────

  describe('openRoleMenu()', () => {
    const open = (role: { id: string; name: string; isSystem: boolean }) => {
      (component as unknown as { roleMenu: { toggle: () => void } }).roleMenu = { toggle: vi.fn() };
      component.openRoleMenu(role, new Event('click'));
      return component.roleMenuItems().map(i => i.label);
    };

    it('offers Edit, permissions and Delete for a custom role', () => {
      expect(open(bartender)).toEqual(['Edit', 'Role permissions', undefined, 'Delete']);
    });

    it('offers Edit and permissions, but not Delete, for the first administrator role', () => {
      expect(open({ id: 'role-admin-id', name: 'ADMINISTRATOR', isSystem: true })).toEqual(['Edit', 'Role permissions']);
    });
  });

  // ── permissions dialog: the first administrator role is read-only ─────────────

  describe('openPermissions() / savePermissions()', () => {
    it('a custom role opens editable', () => {
      component.openPermissions('BARTENDER');
      expect(component.permReadOnly()).toBe(false);
    });

    it('the first administrator role opens read-only', () => {
      component.openPermissions('ADMINISTRATOR', true);
      expect(component.permReadOnly()).toBe(true);
    });

    it('never sends a save for a read-only role (the server would refuse it anyway)', () => {
      const setSpy = vi.spyOn((component as unknown as { permSvc: { setRolePermissions: () => void } }).permSvc, 'setRolePermissions');
      component.openPermissions('ADMINISTRATOR', true);
      component.savePermissions();
      expect(setSpy).not.toHaveBeenCalled();
    });
  });

  // ── saveEdit() ────────────────────────────────────────────────────────────────

  describe('saveEdit()', () => {
    beforeEach(() => {
      component.startEdit(bartender);
    });

    it('calls renameRole with the role id and the trimmed uppercased name', () => {
      component.editingName.set('  mixologist ');
      component.saveEdit();
      expect(mockStaffStore.renameRole).toHaveBeenCalledWith('role-bartender-id', 'MIXOLOGIST');
    });

    it('does nothing when the name is blank', () => {
      component.editingName.set('   ');
      component.saveEdit();
      expect(mockStaffStore.renameRole).not.toHaveBeenCalled();
    });

    it('closes the dialog after success — prevents accidental double-save', () => {
      component.saveEdit();
      expect(component.editingId()).toBeNull();
    });

    it('shows success toast after renaming', () => {
      component.saveEdit();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Role updated.');
    });

    it('clears savingEdit flag after success', () => {
      component.saveEdit();
      expect(component.savingEdit()).toBe(false);
    });

    it('shows error toast on API failure and keeps the dialog open', () => {
      mockStaffStore.renameRole.mockReturnValue(throwError(() => ({ error: { error: 'Name taken.' } })));
      component.saveEdit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Name taken.');
      expect(component.editingId()).toBe('role-bartender-id');
    });

    it('clears savingEdit flag on API failure', () => {
      mockStaffStore.renameRole.mockReturnValue(throwError(() => new Error('Network')));
      component.saveEdit();
      expect(component.savingEdit()).toBe(false);
    });

    it('does nothing when no role is being edited', () => {
      component.cancelEdit();
      component.saveEdit();
      expect(mockStaffStore.renameRole).not.toHaveBeenCalled();
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
      expect(mockToastSvc.success).toHaveBeenCalledWith('Role deleted.');
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
      expect(component.permCatalog()).toEqual(['roles.manage', 'staff.view']);
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
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to load permissions.');
    });
  });

  describe('togglePermission()', () => {
    it('adds a key when checked', () => {
      component.permSelected.set([]);
      component.togglePermission('staff.view', true);
      expect(component.permSelected()).toEqual(['staff.view']);
    });

    it('removes a key when unchecked', () => {
      component.permSelected.set(['staff.view', 'roles.manage']);
      component.togglePermission('staff.view', false);
      expect(component.permSelected()).toEqual(['roles.manage']);
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
      expect(mockToastSvc.success).toHaveBeenCalledWith('Permissions updated.');
      expect(component.permRole()).toBeNull();
    });

    it('toasts saveFailed and keeps the dialog open on error', () => {
      mockPermSvc.setRolePermissions.mockReturnValue(throwError(() => new Error('boom')));
      component.savePermissions();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to save permissions.');
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
