import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { MyLeaveComponent } from './my-leave.component';
import { LeaveService, ToastService, ConfirmService, LeaveRecord } from 'shared-lib';

/**
 * MyLeaveComponent — staff leave management page.
 *
 * WHY these tests matter:
 *  - submit() enforces 4 business rules that protect the data integrity of leave requests.
 *    A bug here could allow overlapping or excessive leave without manager review.
 *  - cancel() requires a confirmation step — a missing or wrong confirmation flow
 *    could let staff accidentally withdraw approved leave.
 *  - onStartDateChange() auto-fills endDate to prevent invalid date ranges in UX.
 */

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Returns a YYYY-MM-DD string offset by `offsetDays` from today (local date). */
function dateStr(offsetDays: number): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  const y   = d.getFullYear();
  const m   = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function makeRecord(overrides: Partial<LeaveRecord> = {}): LeaveRecord {
  return {
    id:         'r1',
    employeeId: 'e1',
    staffId:    'NC001',
    staffName:  'Test User',
    leaveType:  'annual',
    startDate:  '2026-06-01',
    endDate:    '2026-06-05',
    startTime:  '18:00',
    endTime:    '02:00',
    hours:      8,
    notes:      null,
    status:     'pending',
    createdAt:  '2026-05-01T00:00:00Z',
    ...overrides,
  };
}

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('MyLeaveComponent', () => {
  let component: MyLeaveComponent;

  let mockLeaveSvc: {
    getMine:      ReturnType<typeof vi.fn>;
    getMyBalance: ReturnType<typeof vi.fn>;
    apply:        ReturnType<typeof vi.fn>;
    cancel:       ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockLeaveSvc = {
      getMine:      vi.fn().mockReturnValue(of([])),
      getMyBalance: vi.fn().mockReturnValue(of({
        year: 2026, annualEntitlement: 14, annualUsed: 2, annualRemaining: 12,
        sickEntitlement: 14, sickUsed: 0, sickRemaining: 14, unpaidUsed: 0,
      })),
      apply:  vi.fn().mockReturnValue(of({ id: 'new-id' })),
      cancel: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [MyLeaveComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: LeaveService,   useValue: mockLeaveSvc },
        { provide: ToastService,   useValue: mockToastSvc },
        { provide: ConfirmService, useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(MyLeaveComponent);
    component = fixture.componentInstance;
    // Do NOT call detectChanges() — avoids PrimeNG template initialisation errors.
    // All business logic is on the component class; signals and methods are testable directly.
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('fetches leave records so the history tab has data', () => {
      component.ngOnInit();
      expect(mockLeaveSvc.getMine).toHaveBeenCalled();
    });

    it('populates records signal with the fetched data', () => {
      const records = [makeRecord()];
      mockLeaveSvc.getMine.mockReturnValue(of(records));
      component.ngOnInit();
      expect(component.records()).toEqual(records);
    });

    it('fetches leave balance so staff can see their entitlement', () => {
      component.ngOnInit();
      expect(mockLeaveSvc.getMyBalance).toHaveBeenCalled();
    });

    it('populates balance signal with the fetched balance data', () => {
      const balance = {
        year: 2026, annualEntitlement: 14, annualUsed: 2, annualRemaining: 12,
        sickEntitlement: 14, sickUsed: 0, sickRemaining: 14, unpaidUsed: 0,
      };
      mockLeaveSvc.getMyBalance.mockReturnValue(of(balance));
      component.ngOnInit();
      expect(component.balance()).toEqual(balance);
    });

    it('shows error toast when records fail to load', () => {
      mockLeaveSvc.getMine.mockReturnValue(throwError(() => new Error('Network error')));
      component.ngOnInit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to load leave records.');
    });
  });

  // ── submit() — validation rules ──────────────────────────────────────────────

  describe('submit() — validation', () => {
    it('blocks submission and sets error when startDate is missing', () => {
      component.form.startDate  = '';
      component.form.endDate    = '2026-06-05';
      component.form.startTime  = new Date(2026, 5, 1, 10, 0);
      component.form.endTime    = new Date(2026, 5, 5, 18, 0);
      component.submit();
      expect(component.formError()).toBe('Please select start date, end date, and times.');
      expect(mockLeaveSvc.apply).not.toHaveBeenCalled();
    });

    it('blocks submission and sets error when endDate is missing', () => {
      component.form.startDate  = '2026-06-01';
      component.form.endDate    = '';
      component.form.startTime  = new Date(2026, 5, 1, 10, 0);
      component.form.endTime    = new Date(2026, 5, 1, 18, 0);
      component.submit();
      expect(component.formError()).toBe('Please select start date, end date, and times.');
      expect(mockLeaveSvc.apply).not.toHaveBeenCalled();
    });

    it('blocks submission when startTime is missing', () => {
      component.form.startDate  = '2026-06-01';
      component.form.endDate    = '2026-06-05';
      component.form.startTime  = null;
      component.form.endTime    = new Date(2026, 5, 5, 18, 0);
      component.submit();
      expect(component.formError()).toBe('Please select start date, end date, and times.');
      expect(mockLeaveSvc.apply).not.toHaveBeenCalled();
    });

    it('blocks submission when end time is before start time on same date — inverted time range', () => {
      component.form.startDate  = '2026-06-01';
      component.form.endDate    = '2026-06-01';
      component.form.startTime  = new Date(2026, 5, 1, 18, 0);
      component.form.endTime    = new Date(2026, 5, 1, 10, 0); // earlier on same date
      component.submit();
      expect(component.formError()).toBe('End date/time must be after start date/time.');
      expect(mockLeaveSvc.apply).not.toHaveBeenCalled();
    });

    it('blocks submission when duration exceeds 720 hours — single requests are capped at 30 days', () => {
      component.form.startDate  = dateStr(0);
      component.form.endDate    = dateStr(31); // 31 days = 744h > 720h cap
      component.form.startTime  = new Date(2026, 5, 1, 10, 0);
      component.form.endTime    = new Date(2026, 5, 1, 10, 0);
      component.submit();
      expect(component.formError()).toBe('Leave duration cannot exceed 720 hours (30 days) in a single request.');
      expect(mockLeaveSvc.apply).not.toHaveBeenCalled();
    });

    it('blocks submission when start date is more than 1 year in advance', () => {
      component.form.startDate  = dateStr(400);
      component.form.endDate    = dateStr(401);
      component.form.startTime  = new Date(2026, 5, 1, 10, 0);
      component.form.endTime    = new Date(2026, 5, 1, 18, 0);
      component.submit();
      expect(component.formError()).toBe('Leave cannot be requested more than 1 year in advance.');
      expect(mockLeaveSvc.apply).not.toHaveBeenCalled();
    });

    it('clears a stale formError before re-running validation on each submit()', () => {
      component.formError.set('old error from previous attempt');
      component.form.startDate = '';
      component.submit();
      // Error is cleared then set to the new validation message — old text never survives
      expect(component.formError()).toBe('Please select start date, end date, and times.');
    });
  });

  // ── submit() — success path ──────────────────────────────────────────────────

  describe('submit() — success', () => {
    beforeEach(() => {
      component.form.startDate  = dateStr(1);
      component.form.endDate    = dateStr(2);
      component.form.startTime  = new Date(2026, 5, 1, 10, 0);  // 10:00 AM
      component.form.endTime    = new Date(2026, 5, 2, 18, 0);  // 18:00 PM next day → 32h
      component.form.leaveType  = 'annual';
      component.form.notes      = 'Family trip';
    });

    it('calls apply() with the correct payload built from the form', () => {
      component.submit();
      expect(mockLeaveSvc.apply).toHaveBeenCalledWith(
        expect.objectContaining({
          leaveType: 'annual',
          startDate: dateStr(1),
          endDate:   dateStr(2),
          startTime: '10:00',
          endTime:   '18:00',
          notes:     'Family trip',
        }),
      );
    });

    it('converts empty notes to null so the API does not receive empty strings', () => {
      component.form.notes = '';
      component.submit();
      expect(mockLeaveSvc.apply).toHaveBeenCalledWith(
        expect.objectContaining({ notes: null }),
      );
    });

    it('resets the form to defaults so staff can start a new request', () => {
      component.submit();
      expect(component.form.startDate).toBe('');
      expect(component.form.endDate).toBe('');
      expect(component.form.startTime).toBeNull();
      expect(component.form.endTime).toBeNull();
      expect(component.form.notes).toBe('');
      expect(component.form.leaveType).toBe('annual');
    });

    it('reloads leave records so the history tab reflects the new request', () => {
      mockLeaveSvc.getMine.mockClear();
      component.submit();
      expect(mockLeaveSvc.getMine).toHaveBeenCalled();
    });

    it('shows a success toast confirming the request was submitted', () => {
      component.submit();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave request submitted.');
    });

    it('switches to the history view so staff can see the pending request', () => {
      component.submit();
      expect(component.view()).toBe('history');
    });

    it('clears saving flag after success so the button is re-enabled', () => {
      component.submit();
      expect(component.saving()).toBe(false);
    });
  });

  // ── submit() — error path ────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.form.startDate = dateStr(1);
      component.form.endDate   = dateStr(2);
      component.form.startTime = new Date(2026, 5, 1, 10, 0);
      component.form.endTime   = new Date(2026, 5, 2, 18, 0);
    });

    it('shows the API error message in formError so staff know why it failed', () => {
      mockLeaveSvc.apply.mockReturnValue(
        throwError(() => ({ error: { error: 'Insufficient annual leave balance.' } })),
      );
      component.submit();
      expect(component.formError()).toBe('Insufficient annual leave balance.');
    });

    it('falls back to a generic message when the API does not provide one', () => {
      mockLeaveSvc.apply.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.formError()).toBe('Submission failed. Please try again.');
    });

    it('clears saving flag after an error so the submit button is re-enabled', () => {
      mockLeaveSvc.apply.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.saving()).toBe(false);
    });
  });

  // ── cancel() ─────────────────────────────────────────────────────────────────

  describe('cancel()', () => {
    it('shows a confirm dialog before doing anything — prevents accidental cancellation', () => {
      component.cancel(makeRecord());
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('uses "Withdraw Approved Leave" title for approved leave — sets correct manager expectations', () => {
      component.cancel(makeRecord({ status: 'approved' }));
      const opts = mockConfirmSvc.confirm.mock.calls[0][0];
      expect(opts.title).toBe('Withdraw Approved Leave');
    });

    it('uses "Cancel Leave Request" title for pending leave', () => {
      component.cancel(makeRecord({ status: 'pending' }));
      const opts = mockConfirmSvc.confirm.mock.calls[0][0];
      expect(opts.title).toBe('Cancel Leave Request');
    });

    it('marks the dialog as dangerous — cancel uses a red confirm button', () => {
      component.cancel(makeRecord());
      const opts = mockConfirmSvc.confirm.mock.calls[0][0];
      expect(opts.danger).toBe(true);
    });

    it('calls leaveSvc.cancel with the record id when user confirms', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.cancel(makeRecord({ id: 'leave-42' }));
      expect(mockLeaveSvc.cancel).toHaveBeenCalledWith('leave-42');
    });

    it('shows success toast after successful cancellation', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.cancel(makeRecord());
      expect(mockToastSvc.success).toHaveBeenCalledWith('Leave request withdrawn.');
    });

    it('reloads records after cancellation so the list is up to date', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.getMine.mockClear();
      component.cancel(makeRecord());
      expect(mockLeaveSvc.getMine).toHaveBeenCalled();
    });

    it('does NOT call leaveSvc.cancel when user rejects the dialog', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.cancel(makeRecord());
      expect(mockLeaveSvc.cancel).not.toHaveBeenCalled();
    });

    it('shows generic error toast on cancel API failure', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.cancel.mockReturnValue(
        throwError(() => ({ error: { error: 'Unknown server error' } })),
      );
      component.cancel(makeRecord());
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to cancel leave.');
    });

    it('shows "already ended" message when the API says the leave period has ended', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockLeaveSvc.cancel.mockReturnValue(
        throwError(() => ({ error: { error: 'Leave has already ended' } })),
      );
      component.cancel(makeRecord());
      expect(mockToastSvc.error).toHaveBeenCalledWith(
        'This leave has already ended and cannot be cancelled. Contact your manager.',
      );
    });
  });

  // ── minStartDate / minEndDate (Item 3 — min date enforcement) ───────────────

  describe('minStartDate', () => {
    afterEach(() => vi.useRealTimers());

    it('returns today+3 days for annual leave so staff book in advance', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 23, 12, 0, 0)); // 2026-05-23
      component.form.leaveType = 'annual';
      const min = component.minStartDate;
      const expected = new Date(2026, 4, 26); // +3 days
      expected.setHours(0, 0, 0, 0);
      expect(min.getFullYear()).toBe(expected.getFullYear());
      expect(min.getMonth()).toBe(expected.getMonth());
      expect(min.getDate()).toBe(expected.getDate());
    });

    it('returns today for sick leave so staff can log current illness', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 23, 12, 0, 0)); // 2026-05-23
      component.form.leaveType = 'sick';
      const min = component.minStartDate;
      expect(min.getFullYear()).toBe(2026);
      expect(min.getMonth()).toBe(4);    // May
      expect(min.getDate()).toBe(23);
    });

    it('returns today for emergency leave so staff can log urgent absence', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 23, 12, 0, 0));
      component.form.leaveType = 'emergency';
      const min = component.minStartDate;
      expect(min.getDate()).toBe(23);
    });

    it('returns today for unpaid leave', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 23, 12, 0, 0));
      component.form.leaveType = 'unpaid';
      const min = component.minStartDate;
      expect(min.getDate()).toBe(23);
    });
  });

  describe('minEndDate', () => {
    afterEach(() => vi.useRealTimers());

    it('returns the startDate value when startDate is set — end cannot be before start', () => {
      component.form.startDate = '2026-06-10';
      const min = component.minEndDate;
      expect(min.getFullYear()).toBe(2026);
      expect(min.getMonth()).toBe(5);   // June
      expect(min.getDate()).toBe(10);
    });

    it('falls back to minStartDate when startDate is empty', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 23, 12, 0, 0));
      component.form.startDate = '';
      component.form.leaveType = 'sick';
      const min = component.minEndDate;
      expect(min.getDate()).toBe(23);   // today, same as minStartDate for sick
    });
  });

  // ── onStartDateChange() ──────────────────────────────────────────────────────

  describe('onStartDateChange()', () => {
    it('auto-fills endDate from startDate when endDate is empty — prevents invalid range on UX', () => {
      component.form.endDate = '';
      component.onStartDateChange('2026-07-15');
      expect(component.form.endDate).toBe('2026-07-15');
    });

    it('updates endDate when the new startDate is later than the current endDate', () => {
      component.form.endDate = '2026-07-01';
      component.onStartDateChange('2026-07-15');
      expect(component.form.endDate).toBe('2026-07-15');
    });

    it('leaves endDate unchanged when it is already on or after the new startDate', () => {
      component.form.endDate = '2026-07-20';
      component.onStartDateChange('2026-07-15');
      expect(component.form.endDate).toBe('2026-07-20');
    });
  });
});
