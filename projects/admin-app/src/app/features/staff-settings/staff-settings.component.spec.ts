import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { StaffSettingsComponent } from './staff-settings.component';
import {
  StaffConfigService, ToastService, ConfirmService,
  StaffWorkTypeDto, StaffSettlementDto, StaffExpenseCategoryDto, StaffLoanTypeDto,
} from 'shared-lib';

/**
 * StaffSettingsComponent — admin configuration panel for work types, settlements,
 * expense categories, and loan types.
 *
 * WHY these tests matter:
 *  - saveWt() / saveSt() / saveEc() / saveLt() validation rejects empty key or label
 *    before hitting the API — invalid entries would corrupt pick lists used in the
 *    ledger for all staff entries.
 *  - Create vs Update branching must route to the correct configSvc method — calling
 *    createWorkType() when editing would duplicate a record.
 *  - removeWt() / removeSt() etc. require confirmation — deleting a work type that has
 *    existing ledger entries breaks historical record integrity.
 *  - removeLt() must be a no-op for system loan types (isSystem=true).
 *  - severityType() must restrict to known severity values — an unknown value would
 *    crash the PrimeNG Tag component.
 */

function makeWorkType(overrides: Partial<StaffWorkTypeDto> = {}): StaffWorkTypeDto {
  return { id: 'wt1', key: 'bartending', label: 'Bartending', autoCalc: false, hasPrivate: false, displayOrder: 0, isActive: true, ...overrides };
}

function makeSettlement(overrides: Partial<StaffSettlementDto> = {}): StaffSettlementDto {
  return { id: 'st1', key: 'cash', label: 'Cash', severity: 'success', displayOrder: 0, isActive: true, ...overrides };
}

function makeExpenseCategory(overrides: Partial<StaffExpenseCategoryDto> = {}): StaffExpenseCategoryDto {
  return { id: 'ec1', key: 'transport', label: 'Transport', displayOrder: 0, isActive: true, ...overrides };
}

function makeLoanType(overrides: Partial<StaffLoanTypeDto> = {}): StaffLoanTypeDto {
  return { id: 'lt1', key: 'advance', label: 'Advance', isAdvance: true, isSystem: false, displayOrder: 0, isActive: true, ...overrides };
}

describe('StaffSettingsComponent', () => {
  let component: StaffSettingsComponent;

  let mockConfigSvc: {
    workTypes:            ReturnType<typeof vi.fn>;
    settlements:          ReturnType<typeof vi.fn>;
    expenseCategories:    ReturnType<typeof vi.fn>;
    loanTypes:            ReturnType<typeof vi.fn>;
    loadWorkTypes:        ReturnType<typeof vi.fn>;
    loadSettlements:      ReturnType<typeof vi.fn>;
    loadExpenseCategories: ReturnType<typeof vi.fn>;
    loadLoanTypes:        ReturnType<typeof vi.fn>;
    createWorkType:       ReturnType<typeof vi.fn>;
    updateWorkType:       ReturnType<typeof vi.fn>;
    deleteWorkType:       ReturnType<typeof vi.fn>;
    createSettlement:     ReturnType<typeof vi.fn>;
    updateSettlement:     ReturnType<typeof vi.fn>;
    deleteSettlement:     ReturnType<typeof vi.fn>;
    createExpenseCategory: ReturnType<typeof vi.fn>;
    updateExpenseCategory: ReturnType<typeof vi.fn>;
    deleteExpenseCategory: ReturnType<typeof vi.fn>;
    createLoanType:       ReturnType<typeof vi.fn>;
    updateLoanType:       ReturnType<typeof vi.fn>;
    deleteLoanType:       ReturnType<typeof vi.fn>;
  };
  let mockToast:      { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockConfigSvc = {
      workTypes:            vi.fn().mockReturnValue([]),
      settlements:          vi.fn().mockReturnValue([]),
      expenseCategories:    vi.fn().mockReturnValue([]),
      loanTypes:            vi.fn().mockReturnValue([]),
      loadWorkTypes:        vi.fn(),
      loadSettlements:      vi.fn(),
      loadExpenseCategories: vi.fn(),
      loadLoanTypes:        vi.fn(),
      createWorkType:       vi.fn().mockReturnValue(of({ id: 'new-wt' })),
      updateWorkType:       vi.fn().mockReturnValue(of(undefined)),
      deleteWorkType:       vi.fn().mockReturnValue(of(undefined)),
      createSettlement:     vi.fn().mockReturnValue(of({ id: 'new-st' })),
      updateSettlement:     vi.fn().mockReturnValue(of(undefined)),
      deleteSettlement:     vi.fn().mockReturnValue(of(undefined)),
      createExpenseCategory: vi.fn().mockReturnValue(of({ id: 'new-ec' })),
      updateExpenseCategory: vi.fn().mockReturnValue(of(undefined)),
      deleteExpenseCategory: vi.fn().mockReturnValue(of(undefined)),
      createLoanType:       vi.fn().mockReturnValue(of({ id: 'new-lt' })),
      updateLoanType:       vi.fn().mockReturnValue(of(undefined)),
      deleteLoanType:       vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast      = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [StaffSettingsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: StaffConfigService, useValue: mockConfigSvc },
        { provide: ToastService,       useValue: mockToast },
        { provide: ConfirmService,     useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(StaffSettingsComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads all four configuration lists on init', () => {
      component.ngOnInit();
      expect(mockConfigSvc.loadWorkTypes).toHaveBeenCalled();
      expect(mockConfigSvc.loadSettlements).toHaveBeenCalled();
      expect(mockConfigSvc.loadExpenseCategories).toHaveBeenCalled();
      expect(mockConfigSvc.loadLoanTypes).toHaveBeenCalled();
    });
  });

  // ── Work Type: openAddWt() / openEditWt() / cancelWt() ───────────────────────

  describe('work type form management', () => {
    it('openAddWt() shows the form and clears editingWt', () => {
      component.editingWt.set(makeWorkType());
      component.openAddWt();
      expect(component.showWtForm()).toBe(true);
      expect(component.editingWt()).toBeNull();
    });

    it('openEditWt() populates wtForm from the selected item', () => {
      const item = makeWorkType({ key: 'bartending', label: 'Bartending' });
      component.openEditWt(item);
      expect(component.wtForm.key).toBe('bartending');
      expect(component.wtForm.label).toBe('Bartending');
    });

    it('openEditWt() sets editingWt to the selected item', () => {
      const item = makeWorkType({ id: 'wt-edit' });
      component.openEditWt(item);
      expect(component.editingWt()?.id).toBe('wt-edit');
    });

    it('cancelWt() hides form and clears editing', () => {
      component.showWtForm.set(true);
      component.editingWt.set(makeWorkType());
      component.cancelWt();
      expect(component.showWtForm()).toBe(false);
      expect(component.editingWt()).toBeNull();
    });
  });

  // ── saveWt() — validation ─────────────────────────────────────────────────────

  describe('saveWt() — validation', () => {
    it('does not call API when key is empty', () => {
      component.wtForm = { key: '', label: 'Valid', autoCalc: false, hasPrivate: false, displayOrder: 0, isActive: true };
      component.saveWt();
      expect(mockConfigSvc.createWorkType).not.toHaveBeenCalled();
    });

    it('does not call API when label is whitespace only', () => {
      component.wtForm = { key: 'valid-key', label: '   ', autoCalc: false, hasPrivate: false, displayOrder: 0, isActive: true };
      component.saveWt();
      expect(mockConfigSvc.createWorkType).not.toHaveBeenCalled();
    });
  });

  // ── saveWt() — create vs update ───────────────────────────────────────────────

  describe('saveWt() — create path', () => {
    beforeEach(() => {
      component.wtForm = { key: 'bartending', label: 'Bartending', autoCalc: false, hasPrivate: false, displayOrder: 1, isActive: true };
      component.editingWt.set(null);
    });

    it('calls createWorkType when not editing', () => {
      component.saveWt();
      expect(mockConfigSvc.createWorkType).toHaveBeenCalledWith(
        expect.objectContaining({ key: 'bartending', label: 'Bartending' }),
      );
    });

    it('shows success toast on create', () => {
      component.saveWt();
      expect(mockToast.success).toHaveBeenCalledWith('Work type saved.');
    });

    it('hides form and clears editing after create', () => {
      component.showWtForm.set(true);
      component.saveWt();
      expect(component.showWtForm()).toBe(false);
      expect(component.editingWt()).toBeNull();
    });

    it('shows error toast and sets wtError on create failure', () => {
      mockConfigSvc.createWorkType.mockReturnValue(
        throwError(() => ({ error: { error: 'Key already exists.' } })),
      );
      component.saveWt();
      expect(mockToast.error).toHaveBeenCalledWith('Key already exists.');
      expect(component.wtError()).toBe('Key already exists.');
    });

    it('clears savingWt after create error', () => {
      mockConfigSvc.createWorkType.mockReturnValue(throwError(() => ({})));
      component.saveWt();
      expect(component.savingWt()).toBe(false);
    });
  });

  describe('saveWt() — update path', () => {
    beforeEach(() => {
      component.wtForm = { key: 'bartending', label: 'Bartending', autoCalc: false, hasPrivate: false, displayOrder: 1, isActive: true };
      component.editingWt.set(makeWorkType({ id: 'wt-existing' }));
    });

    it('calls updateWorkType with the existing id', () => {
      component.saveWt();
      expect(mockConfigSvc.updateWorkType).toHaveBeenCalledWith('wt-existing', expect.any(Object));
    });

    it('does NOT call createWorkType when editing', () => {
      component.saveWt();
      expect(mockConfigSvc.createWorkType).not.toHaveBeenCalled();
    });
  });

  // ── removeWt() ────────────────────────────────────────────────────────────────

  describe('removeWt()', () => {
    it('shows confirm dialog before deletion', () => {
      component.removeWt(makeWorkType());
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls deleteWorkType when confirmed', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.removeWt(makeWorkType({ id: 'wt-del' }));
      expect(mockConfigSvc.deleteWorkType).toHaveBeenCalledWith('wt-del');
    });

    it('shows success toast after deletion', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.removeWt(makeWorkType());
      expect(mockToast.success).toHaveBeenCalledWith('Work type deleted.');
    });

    it('does NOT call deleteWorkType when user cancels', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.removeWt(makeWorkType());
      expect(mockConfigSvc.deleteWorkType).not.toHaveBeenCalled();
    });

    it('shows error toast on delete failure', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockConfigSvc.deleteWorkType.mockReturnValue(
        throwError(() => ({ error: { error: 'Has existing entries.' } })),
      );
      component.removeWt(makeWorkType());
      expect(mockToast.error).toHaveBeenCalledWith('Has existing entries.');
    });
  });

  // ── Settlement: saveSt() ─────────────────────────────────────────────────────

  describe('saveSt() — create path', () => {
    beforeEach(() => {
      component.stForm = { key: 'cash', label: 'Cash', severity: 'success', displayOrder: 0, isActive: true };
      component.editingSt.set(null);
    });

    it('calls createSettlement when not editing', () => {
      component.saveSt();
      expect(mockConfigSvc.createSettlement).toHaveBeenCalledWith(
        expect.objectContaining({ key: 'cash' }),
      );
    });

    it('shows success toast on create', () => {
      component.saveSt();
      expect(mockToast.success).toHaveBeenCalledWith('Settlement saved.');
    });

    it('does not call API when key is empty', () => {
      component.stForm.key = '';
      component.saveSt();
      expect(mockConfigSvc.createSettlement).not.toHaveBeenCalled();
    });
  });

  describe('saveSt() — update path', () => {
    it('calls updateSettlement with existing id', () => {
      component.stForm = { key: 'cash', label: 'Cash', severity: 'success', displayOrder: 0, isActive: true };
      component.editingSt.set(makeSettlement({ id: 'st-existing' }));
      component.saveSt();
      expect(mockConfigSvc.updateSettlement).toHaveBeenCalledWith('st-existing', expect.any(Object));
    });
  });

  // ── removeSt() ───────────────────────────────────────────────────────────────

  describe('removeSt()', () => {
    it('calls deleteSettlement when confirmed', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.removeSt(makeSettlement({ id: 'st-del' }));
      expect(mockConfigSvc.deleteSettlement).toHaveBeenCalledWith('st-del');
    });

    it('does NOT call deleteSettlement when cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.removeSt(makeSettlement());
      expect(mockConfigSvc.deleteSettlement).not.toHaveBeenCalled();
    });
  });

  // ── Expense Category: saveEc() ───────────────────────────────────────────────

  describe('saveEc() — create path', () => {
    beforeEach(() => {
      component.ecForm = { key: 'transport', label: 'Transport', displayOrder: 0, isActive: true };
      component.editingEc.set(null);
    });

    it('calls createExpenseCategory when not editing', () => {
      component.saveEc();
      expect(mockConfigSvc.createExpenseCategory).toHaveBeenCalledWith(
        expect.objectContaining({ key: 'transport' }),
      );
    });

    it('shows success toast', () => {
      component.saveEc();
      expect(mockToast.success).toHaveBeenCalledWith('Category saved.');
    });

    it('does not call API when label is empty', () => {
      component.ecForm.label = '';
      component.saveEc();
      expect(mockConfigSvc.createExpenseCategory).not.toHaveBeenCalled();
    });
  });

  describe('saveEc() — update path', () => {
    it('calls updateExpenseCategory with existing id', () => {
      component.ecForm = { key: 'transport', label: 'Transport', displayOrder: 0, isActive: true };
      component.editingEc.set(makeExpenseCategory({ id: 'ec-existing' }));
      component.saveEc();
      expect(mockConfigSvc.updateExpenseCategory).toHaveBeenCalledWith('ec-existing', expect.any(Object));
    });
  });

  // ── Loan Type: saveLt() ───────────────────────────────────────────────────────

  describe('saveLt() — create path', () => {
    beforeEach(() => {
      component.ltForm = { key: 'advance', label: 'Advance', isAdvance: true, displayOrder: 0, isActive: true };
      component.editingLt.set(null);
    });

    it('calls createLoanType when not editing', () => {
      component.saveLt();
      expect(mockConfigSvc.createLoanType).toHaveBeenCalledWith(
        expect.objectContaining({ key: 'advance' }),
      );
    });

    it('shows success toast', () => {
      component.saveLt();
      expect(mockToast.success).toHaveBeenCalledWith('Loan type saved.');
    });

    it('does not call API when key is empty', () => {
      component.ltForm.key = '';
      component.saveLt();
      expect(mockConfigSvc.createLoanType).not.toHaveBeenCalled();
    });
  });

  describe('saveLt() — update path', () => {
    it('calls updateLoanType with existing id', () => {
      component.ltForm = { key: 'advance', label: 'Advance', isAdvance: true, displayOrder: 0, isActive: true };
      component.editingLt.set(makeLoanType({ id: 'lt-existing' }));
      component.saveLt();
      expect(mockConfigSvc.updateLoanType).toHaveBeenCalledWith('lt-existing', expect.any(Object));
    });
  });

  // ── removeLt() ────────────────────────────────────────────────────────────────

  describe('removeLt()', () => {
    it('is a no-op for system loan types (isSystem=true)', () => {
      const systemLt = makeLoanType({ isSystem: true });
      component.removeLt(systemLt);
      expect(mockConfirmSvc.confirm).not.toHaveBeenCalled();
    });

    it('calls deleteLoanType when confirmed for non-system types', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.removeLt(makeLoanType({ id: 'lt-del', isSystem: false }));
      expect(mockConfigSvc.deleteLoanType).toHaveBeenCalledWith('lt-del');
    });

    it('shows success toast after deleting a loan type', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.removeLt(makeLoanType({ isSystem: false }));
      expect(mockToast.success).toHaveBeenCalledWith('Loan type deleted.');
    });

    it('does NOT call deleteLoanType when cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.removeLt(makeLoanType({ isSystem: false }));
      expect(mockConfigSvc.deleteLoanType).not.toHaveBeenCalled();
    });
  });

  // ── severityType() ────────────────────────────────────────────────────────────

  describe('severityType()', () => {
    it('returns the known severity value as-is', () => {
      expect(component.severityType('success')).toBe('success');
      expect(component.severityType('danger')).toBe('danger');
      expect(component.severityType('warn')).toBe('warn');
      expect(component.severityType('info')).toBe('info');
      expect(component.severityType('contrast')).toBe('contrast');
      expect(component.severityType('secondary')).toBe('secondary');
    });

    it('returns "secondary" for unknown severity values', () => {
      expect(component.severityType('unknown')).toBe('secondary');
      expect(component.severityType('')).toBe('secondary');
    });
  });
});
