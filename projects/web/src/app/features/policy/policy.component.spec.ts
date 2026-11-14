import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { PolicyComponent } from './policy.component';
import { CompanyPolicy, CompanyPolicyService, DEFAULT_POLICY, ToastService } from 'shared-lib';

/**
 * Company policy page: where an administrator sets the working week and leave rules for the whole company.
 *
 * WHY these tests matter:
 *  - A bad policy (no working day, a 0-hour day) would break leave and payroll for everyone, so the
 *    page must refuse it before it reaches the server (which refuses it too).
 *  - The live example is the administrator's only chance to see what a setting does to a real request.
 *  - A save from a stale page must not overwrite a colleague's change silently.
 *  - Leaving with unsaved edits must prompt, or a mis-click discards the whole form.
 */
describe('PolicyComponent', () => {
  let component: PolicyComponent;
  let current: CompanyPolicy;
  let mockPolicySvc: {
    load: ReturnType<typeof vi.fn>;
    save: ReturnType<typeof vi.fn>;
    policy: () => CompanyPolicy;
  };
  let mockToast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    current = { ...DEFAULT_POLICY, version: 3 };
    const policySignal = signal(current);
    mockPolicySvc = {
      load: vi.fn().mockImplementation(() => {
        policySignal.set(current);
        return of(undefined);
      }),
      save: vi.fn().mockReturnValue(of(undefined)),
      policy: policySignal,
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [PolicyComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: CompanyPolicyService, useValue: mockPolicySvc },
        { provide: ToastService, useValue: mockToast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    // Do NOT call detectChanges(): the logic is on the class, and this keeps PrimeNG out of the test.
    component = TestBed.createComponent(PolicyComponent).componentInstance;
    component.ngOnInit();
  });

  afterEach(() => vi.restoreAllMocks());

  // ── loading ──────────────────────────────────────────────────────────────────

  describe('loading', () => {
    it('fills the form from the saved policy and stops showing the skeleton', () => {
      expect(component.loading()).toBe(false);
      expect(component.form.workingDays).toEqual([1, 2, 3, 4, 5]);
      expect(component.form.workdayHours).toBe(8);
      expect(component.form.carryOverMode).toBe('capped');
      expect(component.form.carryOverMaxHours).toBe(14);
    });

    it('a freshly loaded form has no unsaved changes', () => {
      expect(component.hasUnsavedChanges()).toBe(false);
    });
  });

  // ── working days ─────────────────────────────────────────────────────────────

  describe('toggleDay()', () => {
    it('adds a day and keeps the list in weekday order', () => {
      component.toggleDay(6);
      component.toggleDay(7);
      expect(component.form.workingDays).toEqual([1, 2, 3, 4, 5, 6, 7]);
    });

    it('removes a day that is already on', () => {
      component.toggleDay(3);
      expect(component.form.workingDays).toEqual([1, 2, 4, 5]);
      expect(component.isDayOn(3)).toBe(false);
    });

    it('marks the form as changed', () => {
      component.toggleDay(6);
      expect(component.hasUnsavedChanges()).toBe(true);
    });
  });

  // ── validation ───────────────────────────────────────────────────────────────

  describe('validationError', () => {
    it('is empty for the defaults', () => {
      expect(component.validationError()).toBe('');
    });

    it('refuses a week with no working day', () => {
      [1, 2, 3, 4, 5].forEach((d) => component.toggleDay(d));
      expect(component.validationError()).toBe('Choose at least one working day.');
    });

    it('refuses a working day of 0 or more than 24 hours', () => {
      component.form.workdayHours = 0;
      expect(component.validationError()).toBe(
        'Hours per working day must be more than 0 and at most 24.',
      );
      component.form.workdayHours = 25;
      expect(component.validationError()).toBe(
        'Hours per working day must be more than 0 and at most 24.',
      );
      component.form.workdayHours = null;
      expect(component.validationError()).toBe(
        'Hours per working day must be more than 0 and at most 24.',
      );
    });

    it('refuses a notice period outside 0 to 60 days', () => {
      component.form.annualLeaveAdvanceDays = 61;
      expect(component.validationError()).toBe(
        'Annual leave notice must be between 0 and 60 days.',
      );
      component.form.annualLeaveAdvanceDays = -1;
      expect(component.validationError()).toBe(
        'Annual leave notice must be between 0 and 60 days.',
      );
    });

    it('refuses a longest request outside 1 to 366 days', () => {
      component.form.maxLeaveDays = 0;
      expect(component.validationError()).toBe(
        'The longest single leave request must be between 1 and 366 days.',
      );
    });

    it('refuses a divisor outside 20 to 31', () => {
      component.form.unpaidLeaveDivisor = 19;
      expect(component.validationError()).toBe(
        'The unpaid-leave divisor must be between 20 and 31.',
      );
    });

    it('checks the carry-over cap only when carry-over is capped', () => {
      component.form.carryOverMaxHours = 900;
      expect(component.validationError()).toBe(
        'The carry-over cap must be between 0 and 720 hours.',
      );
      component.form.carryOverMode = 'unlimited';
      expect(component.showCarryOverCap()).toBe(false);
      expect(component.validationError()).toBe('');
    });
  });

  // ── live example ─────────────────────────────────────────────────────────────

  describe('examples', () => {
    it('Monday-to-Friday company: Friday to Monday costs one day and a Saturday is refused', () => {
      const [fridayToMonday, saturday] = component.examples();
      expect(fridayToMonday).toContain('16 hours');
      expect(saturday).toContain('not a working day');
    });

    it('adding Saturday makes Friday to Monday cost two days and a Saturday a full day', () => {
      component.toggleDay(6);
      const [fridayToMonday, saturday] = component.examples();
      expect(fridayToMonday).toContain('24 hours');
      expect(saturday).toContain('8 hours');
    });

    it('follows the hours-per-day setting', () => {
      component.form.workdayHours = 6;
      expect(component.examples()[0]).toContain('12 hours');
    });
  });

  // ── saving ───────────────────────────────────────────────────────────────────

  describe('save()', () => {
    it('sends the edited values (no version: the service attaches the loaded one) and confirms', () => {
      component.toggleDay(6);
      component.form.carryOverMode = 'unlimited';
      component.save();

      expect(mockPolicySvc.save).toHaveBeenCalledWith({
        workingDays: [1, 2, 3, 4, 5, 6],
        workdayHours: 8,
        annualLeaveAdvanceDays: 3,
        maxLeaveDays: 30,
        carryOverMode: 'unlimited',
        carryOverMaxHours: 14,
        unpaidLeaveDivisor: 26,
      });
      expect(mockToast.success).toHaveBeenCalledWith('Company policy saved.');
      expect(component.saving()).toBe(false);
    });

    it('after a successful save the form counts as saved', () => {
      component.toggleDay(6);
      component.save();
      expect(component.hasUnsavedChanges()).toBe(false);
    });

    it('does not call the server with an invalid policy and shows why', () => {
      component.form.workdayHours = 0;
      component.save();
      expect(mockPolicySvc.save).not.toHaveBeenCalled();
      expect(component.error()).toBe('Hours per working day must be more than 0 and at most 24.');
    });

    it('a stale save (409) tells the administrator to reload instead of overwriting', () => {
      mockPolicySvc.save.mockReturnValue(
        throwError(() => ({
          status: 409,
          error: { error: 'The policy was changed by someone else.' },
        })),
      );
      component.toggleDay(6);
      component.save();
      expect(component.conflict()).toBe(true);
      expect(component.error()).toContain('changed by someone else');
      expect(component.saving()).toBe(false);
      expect(component.hasUnsavedChanges()).toBe(true); // the administrator's edits are kept on screen
    });

    it('shows the server message when it refuses a value (400)', () => {
      mockPolicySvc.save.mockReturnValue(
        throwError(() => ({ status: 400, error: { error: 'Choose at least one working day.' } })),
      );
      component.toggleDay(6);
      component.save();
      expect(component.error()).toBe('Choose at least one working day.');
      expect(component.conflict()).toBe(false);
    });
  });

  // ── reload ───────────────────────────────────────────────────────────────────

  describe('reload()', () => {
    it('discards the edits, takes the current policy and clears the conflict', () => {
      mockPolicySvc.save.mockReturnValue(
        throwError(() => ({ status: 409, error: { error: 'x' } })),
      );
      component.toggleDay(6);
      component.save();
      current = { ...DEFAULT_POLICY, workingDays: [1, 2, 3, 4], workdayHours: 7, version: 9 };

      component.reload();

      expect(component.conflict()).toBe(false);
      expect(component.error()).toBe('');
      expect(component.form.workingDays).toEqual([1, 2, 3, 4]);
      expect(component.form.workdayHours).toBe(7);
      expect(component.hasUnsavedChanges()).toBe(false);
    });
  });
});
