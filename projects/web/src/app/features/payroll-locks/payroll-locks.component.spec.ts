import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { PayrollLockService, ToastService, ConfirmService } from 'shared-lib';
import { PayrollLocksComponent } from './payroll-locks.component';

describe('PayrollLocksComponent', () => {
  let component: PayrollLocksComponent;
  let svc: { list: ReturnType<typeof vi.fn>; lock: ReturnType<typeof vi.fn>; reopen: ReturnType<typeof vi.fn> };
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let confirmSvc: { confirm: ReturnType<typeof vi.fn> };

  const fakeLocks = [{ year: 2026, month: 5, lockedAt: '2026-05-31T00:00:00Z', lockedBy: 'Mgr', note: null }];

  beforeEach(async () => {
    svc = {
      list:   vi.fn().mockReturnValue(of(fakeLocks)),
      lock:   vi.fn().mockReturnValue(of({ id: 'x' })),
      reopen: vi.fn().mockReturnValue(of(void 0)),
    };
    toast      = { success: vi.fn(), error: vi.fn() };
    confirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [PayrollLocksComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PayrollLockService, useValue: svc },
        { provide: ToastService,       useValue: toast },
        { provide: ConfirmService,     useValue: confirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(PayrollLocksComponent).componentInstance;
  });

  it('loads locked periods on init', () => {
    component.ngOnInit();
    expect(svc.list).toHaveBeenCalled();
    expect(component.locks().length).toBe(1);
  });

  it('locks a period with the form values, then reloads', () => {
    component.form = { year: 2026, month: 6, note: '  payout done  ' };
    component.lockPeriod();
    expect(svc.lock).toHaveBeenCalledWith(2026, 6, 'payout done');
    expect(toast.success).toHaveBeenCalled();
    expect(svc.list).toHaveBeenCalled();   // reload
  });

  it('does not lock when the month is invalid', () => {
    component.form = { year: 2026, month: 0, note: '' };
    component.lockPeriod();
    expect(svc.lock).not.toHaveBeenCalled();
  });

  it('surfaces a toast error when locking fails', () => {
    svc.lock.mockReturnValue(throwError(() => ({ error: { error: 'Already locked.' } })));
    component.form = { year: 2026, month: 7, note: '' };
    component.lockPeriod();
    expect(toast.error).toHaveBeenCalledWith('Already locked.');
  });

  it('reopens after confirmation', () => {
    component.reopen(fakeLocks[0]);
    expect(confirmSvc.confirm).toHaveBeenCalled();
    expect(svc.reopen).toHaveBeenCalledWith(2026, 5);
    expect(toast.success).toHaveBeenCalled();
  });

  it('does NOT reopen when the confirmation is dismissed', () => {
    confirmSvc.confirm.mockReturnValue(of(false));
    component.reopen(fakeLocks[0]);
    expect(svc.reopen).not.toHaveBeenCalled();
  });

  it('formats the period as YYYY-MM', () => {
    expect(component.period(fakeLocks[0])).toBe('2026-05');
  });
});
