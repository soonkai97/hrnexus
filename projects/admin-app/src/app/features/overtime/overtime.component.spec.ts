import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { OvertimeComponent } from './overtime.component';
import { AdminService, StaffStore, ToastService, ConfirmService } from 'shared-lib';

/**
 * OvertimeComponent — admin overtime-rule CRUD.
 *
 * WHY these tests matter:
 *  - save() blocks thresholdHours ≤ 0 — a zero threshold applies OT to every shift,
 *    which would inflate every pay run silently.
 *  - save() blocks multiplier < 1 — a sub-1 multiplier reduces pay for extra hours,
 *    which is a wage-theft risk.
 *  - The role wildcard ('*') vs comma-joined list determines which staff are affected;
 *    a wrong mapping means the wrong roles get OT pay.
 *  - openEdit() must parse a stored comma-string back into an array so the multi-select
 *    renders current values correctly.
 */

function makeRule(overrides = {}): any {
  return {
    id: 'r1', role: 'bartender', thresholdHours: 8,
    multiplier: 1.5, isActive: true, ...overrides,
  };
}

describe('OvertimeComponent', () => {
  let component: OvertimeComponent;

  let mockAdminSvc: {
    getOvertimeRules:   ReturnType<typeof vi.fn>;
    upsertOvertimeRule: ReturnType<typeof vi.fn>;
    deleteOvertimeRule: ReturnType<typeof vi.fn>;
  };
  let mockStaffStore: {
    loadRoles: ReturnType<typeof vi.fn>;
    roles:     ReturnType<typeof vi.fn>;
    loading:   ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAdminSvc = {
      getOvertimeRules:   vi.fn().mockReturnValue(of([])),
      upsertOvertimeRule: vi.fn().mockReturnValue(of(undefined)),
      deleteOvertimeRule: vi.fn().mockReturnValue(of(undefined)),
    };
    mockStaffStore = {
      loadRoles: vi.fn(),
      roles:     vi.fn().mockReturnValue([]),
      loading:   vi.fn().mockReturnValue(false),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [OvertimeComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AdminService,  useValue: mockAdminSvc },
        { provide: StaffStore,    useValue: mockStaffStore },
        { provide: ToastService,  useValue: mockToastSvc },
        { provide: ConfirmService, useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(OvertimeComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads roles so the multi-select dropdown is populated', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadRoles).toHaveBeenCalled();
    });

    it('fetches existing overtime rules on init', () => {
      component.ngOnInit();
      expect(mockAdminSvc.getOvertimeRules).toHaveBeenCalled();
    });

    it('populates rules signal with fetched data', () => {
      const rules = [makeRule()];
      mockAdminSvc.getOvertimeRules.mockReturnValue(of(rules));
      component.ngOnInit();
      expect(component.rules()).toEqual(rules);
    });

    it('shows error toast when rules fail to load', () => {
      mockAdminSvc.getOvertimeRules.mockReturnValue(throwError(() => new Error('Network')));
      component.ngOnInit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to load rules.');
    });
  });

  // ── save() — validation ───────────────────────────────────────────────────────

  describe('save() — validation', () => {
    it('blocks threshold ≤ 0 — would apply OT to every minute worked', () => {
      component.form.thresholdHours = 0;
      component.form.multiplier = 1.5;
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Threshold hours must be greater than zero.');
      expect(mockAdminSvc.upsertOvertimeRule).not.toHaveBeenCalled();
    });

    it('blocks negative threshold', () => {
      component.form.thresholdHours = -1;
      component.form.multiplier = 1.5;
      component.save();
      expect(mockAdminSvc.upsertOvertimeRule).not.toHaveBeenCalled();
    });

    it('blocks multiplier < 1 — would reduce pay, a wage-theft risk', () => {
      component.form.thresholdHours = 8;
      component.form.multiplier = 0.9;
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Multiplier must be at least 1.0 (cannot reduce pay).');
      expect(mockAdminSvc.upsertOvertimeRule).not.toHaveBeenCalled();
    });

    it('allows exactly multiplier = 1.0 — straight time is valid', () => {
      component.form.thresholdHours = 8;
      component.form.multiplier = 1.0;
      component.save();
      expect(mockAdminSvc.upsertOvertimeRule).toHaveBeenCalled();
    });

    it('allows threshold = 1 (minimum positive)', () => {
      component.form.thresholdHours = 1;
      component.form.multiplier = 1.5;
      component.save();
      expect(mockAdminSvc.upsertOvertimeRule).toHaveBeenCalled();
    });
  });

  // ── save() — role wildcard vs comma list ──────────────────────────────────────

  describe('save() — role payload', () => {
    beforeEach(() => {
      component.form.thresholdHours = 8;
      component.form.multiplier = 1.5;
    });

    it('sends "*" when no roles are selected — rule applies to all staff', () => {
      component.selectedRoles.set([]);
      component.save();
      const payload = mockAdminSvc.upsertOvertimeRule.mock.calls[0][0];
      expect(payload.role).toBe('*');
    });

    it('sends comma-joined string when specific roles are selected', () => {
      component.selectedRoles.set(['bartender', 'server']);
      component.save();
      const payload = mockAdminSvc.upsertOvertimeRule.mock.calls[0][0];
      expect(payload.role).toBe('bartender,server');
    });

    it('sends a single role without trailing comma', () => {
      component.selectedRoles.set(['manager']);
      component.save();
      const payload = mockAdminSvc.upsertOvertimeRule.mock.calls[0][0];
      expect(payload.role).toBe('manager');
    });
  });

  // ── save() — create vs update ─────────────────────────────────────────────────

  describe('save() — create vs update path', () => {
    beforeEach(() => {
      component.form.thresholdHours = 8;
      component.form.multiplier = 1.5;
    });

    it('sends no id when creating a new rule', () => {
      component.editingId.set(null);
      component.save();
      const payload = mockAdminSvc.upsertOvertimeRule.mock.calls[0][0];
      expect(payload.id).toBeUndefined();
    });

    it('sends the editing id when updating an existing rule', () => {
      component.editingId.set('r1');
      component.save();
      const payload = mockAdminSvc.upsertOvertimeRule.mock.calls[0][0];
      expect(payload.id).toBe('r1');
    });

    it('shows success toast and reloads after save', () => {
      component.editingId.set(null);
      mockAdminSvc.getOvertimeRules.mockClear();
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalled();
      expect(mockAdminSvc.getOvertimeRules).toHaveBeenCalled();
    });

    it('hides the form after save', () => {
      component.showForm.set(true);
      component.save();
      expect(component.showForm()).toBe(false);
    });

    it('shows error toast and clears saving flag on API failure', () => {
      mockAdminSvc.upsertOvertimeRule.mockReturnValue(throwError(() => new Error('Network')));
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Save failed.');
      expect(component.saving()).toBe(false);
    });
  });

  // ── openEdit() — role parsing ─────────────────────────────────────────────────

  describe('openEdit() — role string parsing', () => {
    it('parses comma-delimited role string into selectedRoles array', () => {
      component.openEdit(makeRule({ role: 'bartender,server,host' }));
      expect(component.selectedRoles()).toEqual(['bartender', 'server', 'host']);
    });

    it('sets selectedRoles to [] when role is the wildcard "*"', () => {
      component.openEdit(makeRule({ role: '*' }));
      expect(component.selectedRoles()).toEqual([]);
    });

    it('sets editingId to the rule id', () => {
      component.openEdit(makeRule({ id: 'r99' }));
      expect(component.editingId()).toBe('r99');
    });

    it('populates form fields with rule values', () => {
      component.openEdit(makeRule({ thresholdHours: 10, multiplier: 2, isActive: false }));
      expect(component.form.thresholdHours).toBe(10);
      expect(component.form.multiplier).toBe(2);
      expect(component.form.isActive).toBe(false);
    });

    it('shows the form panel', () => {
      component.openEdit(makeRule());
      expect(component.showForm()).toBe(true);
    });
  });

  // ── delete() ─────────────────────────────────────────────────────────────────

  describe('delete()', () => {
    it('shows a confirmation dialog before deleting', () => {
      component.delete('r1');
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls deleteOvertimeRule when confirmed', () => {
      component.delete('r1');
      expect(mockAdminSvc.deleteOvertimeRule).toHaveBeenCalledWith('r1');
    });

    it('shows success toast after deletion', () => {
      component.delete('r1');
      expect(mockToastSvc.success).toHaveBeenCalledWith('Rule deleted.');
    });

    it('reloads rules after deletion', () => {
      mockAdminSvc.getOvertimeRules.mockClear();
      component.delete('r1');
      expect(mockAdminSvc.getOvertimeRules).toHaveBeenCalled();
    });

    it('does NOT call deleteOvertimeRule when confirmation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.delete('r1');
      expect(mockAdminSvc.deleteOvertimeRule).not.toHaveBeenCalled();
    });

    it('shows error toast when deletion fails', () => {
      mockAdminSvc.deleteOvertimeRule.mockReturnValue(throwError(() => new Error('Network')));
      component.delete('r1');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Delete failed.');
    });
  });
});
