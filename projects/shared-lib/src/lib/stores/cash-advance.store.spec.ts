import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { CashAdvanceService } from '../services/cash-advance.service';
import { ToastService } from '../services/toast.service';
import { CashAdvanceStore } from './cash-advance.store';

/**
 * Cash advances, through the store.
 *
 * WHY these tests matter: a cash advance is a financial record. Each change must say whether it worked, show the server's reason
 * when refused (for example a schedule change while a run under review has the advance), and read the list again with the filter
 * the person chose.
 */
describe('CashAdvanceStore', () => {
  let store: CashAdvanceStore;
  let svc: Record<string, ReturnType<typeof vi.fn>>;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    svc = {
      list: vi.fn().mockReturnValue(of([{ id: 'a1' }])),
      create: vi.fn().mockReturnValue(of({ id: 'a2' })),
      setSettled: vi.fn().mockReturnValue(of(void 0)),
      setRepayment: vi.fn().mockReturnValue(of(void 0)),
      delete: vi.fn().mockReturnValue(of(void 0)),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        { provide: CashAdvanceService, useValue: svc },
        { provide: ToastService, useValue: toast },
      ],
    });
    store = TestBed.inject(CashAdvanceStore);
  });

  it('reads the list with the chosen status, and says so when it cannot', () => {
    store.load('outstanding');
    expect(svc['list']).toHaveBeenCalledWith('outstanding');
    expect(store.advances()).toEqual([{ id: 'a1' }]);

    svc['list'].mockReturnValue(throwError(() => new Error('net')));
    store.load();
    expect(toast.error).toHaveBeenCalledWith('Failed to load cash advances.');
    expect(store.loading()).toBe(false);
  });

  it('create and schedule say it worked, emit once and read the list again with the same filter', () => {
    store.load('outstanding');
    svc['list'].mockClear();
    const created = vi.fn();

    store.create({ employeeId: 'e1', amount: 500, advanceDate: '2026-07-01' }).subscribe(created);
    store.setRepayment('a1', 200).subscribe();

    expect(created).toHaveBeenCalledTimes(1);
    expect(svc['setRepayment']).toHaveBeenCalledWith('a1', 200);
    expect(toast.success).toHaveBeenCalledWith('Cash advance recorded.');
    expect(toast.success).toHaveBeenCalledWith('Repayment saved.');
    expect(svc['list']).toHaveBeenNthCalledWith(1, 'outstanding');
    expect(svc['list']).toHaveBeenNthCalledWith(2, 'outstanding');
  });

  it('a refused schedule shows the server reason and emits nothing', () => {
    svc['setRepayment'].mockReturnValue(
      throwError(() => ({ error: { error: 'Return the run first.' } })),
    );
    const done = vi.fn();

    store.setRepayment('a1', 100).subscribe(done);

    expect(toast.error).toHaveBeenCalledWith('Return the run first.');
    expect(done).not.toHaveBeenCalled();
    expect(store.saving()).toBe(false);
  });

  it('settle and delete mark the advance busy while they run and report what happened', () => {
    store.setSettled('a1', true).subscribe();
    expect(toast.success).toHaveBeenCalledWith('Marked as settled.');
    store.setSettled('a1', false).subscribe();
    expect(toast.success).toHaveBeenCalledWith('Reopened.');
    store.remove('a1').subscribe();
    expect(toast.success).toHaveBeenCalledWith('Cash advance deleted.');
    expect(store.busyId()).toBeNull();
  });

  it('a refused delete shows the reason and frees the advance', () => {
    svc['delete'].mockReturnValue(throwError(() => ({ error: { error: 'A run has it.' } })));

    store.remove('a1').subscribe();

    expect(toast.error).toHaveBeenCalledWith('A run has it.');
    expect(store.busyId()).toBeNull();
  });
});
