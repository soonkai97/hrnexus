import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ConfirmService, PayItem, PayrollService, ToastService } from 'shared-lib';
import { PayItemsComponent } from './pay-items.component';

/**
 * A person's recurring allowances and deductions.
 *
 * WHY these tests matter: an item changes someone's pay every month until it ends. The form must refuse an empty reason or a
 * zero amount before it reaches the server, a deduction must carry its priority and an allowance must not, and the server's
 * reason (an item a locked month already used cannot be deleted, or start in a paid month) must reach the person.
 */
describe('PayItemsComponent', () => {
  let component: PayItemsComponent;
  let svc: Record<string, ReturnType<typeof vi.fn>>;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let confirmSvc: { confirm: ReturnType<typeof vi.fn> };

  const item = (over: Partial<PayItem> = {}): PayItem => ({
    id: 'i1',
    kind: 'allowance',
    reason: 'Transport',
    amount: 150,
    countsForEpf: true,
    countsForSocsoEis: false,
    priority: 0,
    from: '2026-01-01',
    to: null,
    createdAt: 'x',
    inUse: false,
    ...over,
  });

  beforeEach(async () => {
    svc = {
      getPayItems: vi.fn().mockReturnValue(of([item()])),
      addPayItem: vi.fn().mockReturnValue(of({ id: 'new' })),
      endPayItem: vi.fn().mockReturnValue(of(void 0)),
      deletePayItem: vi.fn().mockReturnValue(of(void 0)),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    confirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };
    await TestBed.configureTestingModule({
      imports: [PayItemsComponent],
      providers: [
        provideHttpClient(),
        { provide: PayrollService, useValue: svc },
        { provide: ToastService, useValue: toast },
        { provide: ConfirmService, useValue: confirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
    component = TestBed.createComponent(PayItemsComponent).componentInstance;
    component.staffId = 's1';
    component.canManage = true;
  });

  it('loads the items of the person when the id arrives', () => {
    component.ngOnChanges({
      staffId: {
        currentValue: 's1',
        previousValue: '',
        firstChange: true,
        isFirstChange: () => true,
      },
    });

    expect(svc['getPayItems']).toHaveBeenCalledWith('s1');
    expect(component.items()).toHaveLength(1);
  });

  it('explains what an allowance counts towards', () => {
    expect(component.countsText(item({ countsForEpf: true, countsForSocsoEis: true }))).toBe(
      'EPF and SOCSO and EIS',
    );
    expect(component.countsText(item({ countsForEpf: true, countsForSocsoEis: false }))).toBe(
      'EPF',
    );
    expect(component.countsText(item({ countsForEpf: false, countsForSocsoEis: false }))).toBe(
      'neither EPF nor SOCSO',
    );
  });

  it('knows an item with a past end date has ended', () => {
    expect(component.isEnded(item({ to: '2000-01-01' }))).toBe(true);
    expect(component.isEnded(item({ to: '2999-01-01' }))).toBe(false);
    expect(component.isEnded(item({ to: null }))).toBe(false);
  });

  it('refuses an empty reason, a zero amount and a missing start date before asking the server', () => {
    component.openAdd();
    component.form.reason = ' ';
    component.form.amount = 100;
    expect(component.isFormValid).toBe(false);

    component.form.reason = 'Transport';
    component.form.amount = 0;
    expect(component.isFormValid).toBe(false);

    component.form.amount = 100;
    component.form.from = '';
    expect(component.isFormValid).toBe(false);

    component.form.from = '2026-01-01';
    expect(component.isFormValid).toBe(true);
    component.form.reason = '';
    component.add();
    expect(svc['addPayItem']).not.toHaveBeenCalled();
  });

  it('adds an allowance with its contribution flags and no priority', () => {
    component.openAdd();
    component.form.reason = ' Transport ';
    component.form.amount = 150;
    component.form.from = '2026-01-01';
    component.form.priority = 9;

    component.add();

    expect(svc['addPayItem']).toHaveBeenCalledWith('s1', {
      kind: 'allowance',
      reason: 'Transport',
      amount: 150,
      countsForEpf: true,
      countsForSocsoEis: true,
      priority: 0,
      from: '2026-01-01',
      to: null,
    });
    expect(component.showAdd()).toBe(false);
  });

  it('adds a deduction with its priority', () => {
    component.openAdd();
    component.form.kind = 'deduction';
    component.form.reason = 'Parking';
    component.form.amount = 50;
    component.form.from = '2026-01-01';
    component.form.priority = 3;
    component.form.to = '2026-12-31';

    component.add();

    expect(svc['addPayItem']).toHaveBeenCalledWith(
      's1',
      expect.objectContaining({ kind: 'deduction', priority: 3, to: '2026-12-31' }),
    );
  });

  it('shows the server reason when an item is refused, and keeps the dialog open', () => {
    svc['addPayItem'].mockReturnValue(
      throwError(() => ({
        error: {
          error:
            'A payroll run that is already submitted or locked covers that month for this person, so the item cannot start then.',
        },
      })),
    );
    component.openAdd();
    component.form.reason = 'Transport';
    component.form.amount = 150;
    component.form.from = '2026-10-01';

    component.add();

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('cannot start then'));
    expect(component.showAdd()).toBe(true);
    expect(component.saving()).toBe(false);
  });

  it('ends an item on the chosen day', () => {
    component.openEnd(item());
    component.endDate = '2026-12-31';

    component.end();

    expect(svc['endPayItem']).toHaveBeenCalledWith('s1', 'i1', '2026-12-31');
    expect(component.endTarget()).toBeNull();
  });

  it('deleting asks first and does nothing when the person says no', () => {
    confirmSvc.confirm.mockReturnValue(of(false));

    component.remove(item());

    expect(svc['deletePayItem']).not.toHaveBeenCalled();
  });

  it('a delete the server refuses (a locked month used the item) shows why', () => {
    svc['deletePayItem'].mockReturnValue(
      throwError(() => ({
        error: {
          error:
            'A payroll run that is already submitted or locked used this item, so it cannot be deleted. End it instead.',
        },
      })),
    );

    component.remove(item());

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('End it instead'));
  });
});
