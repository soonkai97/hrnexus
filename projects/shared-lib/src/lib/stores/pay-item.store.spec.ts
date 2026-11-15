import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { PayrollService } from '../services/payroll.service';
import { ToastService } from '../services/toast.service';
import { PayItemStore } from './pay-item.store';

/**
 * A person's recurring pay items, through the store.
 *
 * WHY these tests matter: the page must never call the API. Every change must say that it worked or why it did not, read the
 * list again so the page matches the server, and never let a slow answer for another person replace the list on screen.
 */
describe('PayItemStore', () => {
  let store: PayItemStore;
  let svc: Record<string, ReturnType<typeof vi.fn>>;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    svc = {
      getPayItems: vi.fn().mockReturnValue(of([{ id: 'i1' }])),
      addPayItem: vi.fn().mockReturnValue(of({ id: 'i2' })),
      endPayItem: vi.fn().mockReturnValue(of(void 0)),
      deletePayItem: vi.fn().mockReturnValue(of(void 0)),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        { provide: PayrollService, useValue: svc },
        { provide: ToastService, useValue: toast },
      ],
    });
    store = TestBed.inject(PayItemStore);
  });

  it("reads a person's items, and says why when it cannot", () => {
    store.load('s1');
    expect(store.items()).toEqual([{ id: 'i1' }]);

    svc['getPayItems'].mockReturnValue(throwError(() => ({ error: { error: 'No.' } })));
    store.load('s1');
    expect(toast.error).toHaveBeenCalledWith('No.');
  });

  it('drops a slow answer for a person the page already left', () => {
    const slow = new Subject<unknown[]>();
    svc['getPayItems'].mockReturnValue(slow);
    store.load('s1');
    svc['getPayItems'].mockReturnValue(of([{ id: 'other' }]));
    store.load('s2');

    slow.next([{ id: 'late' }]);

    expect(store.items()).toEqual([{ id: 'other' }]);
  });

  it('add, end and remove call the service for that person, say it worked and read the list again', () => {
    store.load('s1');
    svc['getPayItems'].mockClear();
    const added = vi.fn();

    store.add('s1', { reason: 'Transport' } as never).subscribe(added);
    store.end('s1', 'i1', '2026-12-31').subscribe();
    store.remove('s1', 'i1').subscribe();

    expect(svc['addPayItem']).toHaveBeenCalledWith('s1', { reason: 'Transport' });
    expect(svc['endPayItem']).toHaveBeenCalledWith('s1', 'i1', '2026-12-31');
    expect(svc['deletePayItem']).toHaveBeenCalledWith('s1', 'i1');
    expect(added).toHaveBeenCalledTimes(1);
    expect(toast.success).toHaveBeenCalledWith('Added.');
    expect(toast.success).toHaveBeenCalledWith('End date saved.');
    expect(toast.success).toHaveBeenCalledWith('Deleted.');
    expect(svc['getPayItems']).toHaveBeenCalledTimes(3);
  });

  it('a refused change shows the server reason and emits nothing', () => {
    svc['deletePayItem'].mockReturnValue(
      throwError(() => ({ error: { error: 'A run has used this item.' } })),
    );
    const done = vi.fn();

    store.remove('s1', 'i1').subscribe(done);

    expect(toast.error).toHaveBeenCalledWith('A run has used this item.');
    expect(done).not.toHaveBeenCalled();
    expect(store.saving()).toBe(false);
  });

  it('saves one change at a time', () => {
    svc['addPayItem'].mockReturnValue(new Subject()); // never answers
    store.add('s1', {} as never).subscribe();
    const sent = vi.fn();
    svc['deletePayItem'].mockReturnValue(new Observable(() => sent())); // only sent when subscribed to

    store.remove('s1', 'i1').subscribe();

    expect(sent).not.toHaveBeenCalled();
    expect(store.saving()).toBe(true);
  });
});
