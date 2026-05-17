import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { RolesComponent } from './roles.component';
import { StaffStore, ToastService, ConfirmService } from 'shared-lib';

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

    await TestBed.configureTestingModule({
      imports: [RolesComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: StaffStore,    useValue: mockStaffStore },
        { provide: ToastService,  useValue: mockToastSvc },
        { provide: ConfirmService, useValue: mockConfirmSvc },
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

    it('calls createRole with the trimmed name and rate', () => {
      component.newRoleName = '  bartender  ';
      component.newRoleRate = 15;
      component.addRole();
      expect(mockStaffStore.createRole).toHaveBeenCalledWith('bartender', 15);
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

    it('sets error signal on API failure', () => {
      mockStaffStore.createRole.mockReturnValue(throwError(() => ({ error: { error: 'Role exists.' } })));
      component.newRoleName = 'bartender';
      component.addRole();
      expect(component.error()).toBe('Role exists.');
    });

    it('clears saving flag on API failure', () => {
      mockStaffStore.createRole.mockReturnValue(throwError(() => new Error('Network')));
      component.newRoleName = 'host';
      component.addRole();
      expect(component.saving()).toBe(false);
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
      expect(mockStaffStore.updateRoleRate).toHaveBeenCalledWith('bartender', 15);
    });

    it('clears editingRateName after success — prevents accidental double-save', () => {
      component.saveRate();
      expect(component.editingRateName()).toBeNull();
    });

    it('shows success toast after updating the rate', () => {
      component.saveRate();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Hourly rate updated.');
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
      expect(mockToastSvc.success).toHaveBeenCalledWith('Role deleted.');
    });

    it('does NOT delete when confirmation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.deleteRole('bartender');
      expect(mockStaffStore.deleteRole).not.toHaveBeenCalled();
    });

    it('sets error signal when deletion fails', () => {
      mockStaffStore.deleteRole.mockReturnValue(throwError(() => ({ error: { error: 'Role in use.' } })));
      component.deleteRole('bartender');
      expect(component.error()).toBe('Role in use.');
    });
  });
});
