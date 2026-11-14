import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { CashAdvanceService, StaffStore, ToastService, ConfirmService } from 'shared-lib';
import { CashAdvancesComponent } from './cash-advances.component';

/**
 * CashAdvancesComponent (admin record + settle). WHY these matter:
 *  - recording money must be gated on a valid form (staff + positive amount);
 *  - the settle toggle must flip to the OPPOSITE of the current status;
 *  - delete must only fire after the confirm dialog is accepted.
 */
describe('CashAdvancesComponent', () => {
  let mockSvc: any;
  let mockStore: any;
  let mockToast: any;
  let mockConfirm: any;

  function make(): CashAdvancesComponent {
    return TestBed.createComponent(CashAdvancesComponent).componentInstance;
  }

  beforeEach(async () => {
    mockSvc = {
      list:       vi.fn().mockReturnValue(of([])),
      create:     vi.fn().mockReturnValue(of({ id: 'a1' })),
      setSettled: vi.fn().mockReturnValue(of(undefined)),
      delete:     vi.fn().mockReturnValue(of(undefined)),
    };
    mockStore   = { staff: vi.fn().mockReturnValue([]), loadStaff: vi.fn() };
    mockToast   = { success: vi.fn(), error: vi.fn() };
    mockConfirm = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [CashAdvancesComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: CashAdvanceService, useValue: mockSvc },
        { provide: StaffStore,         useValue: mockStore },
        { provide: ToastService,       useValue: mockToast },
        { provide: ConfirmService,     useValue: mockConfirm },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  it('loads advances and staff on init', () => {
    const c = make();
    c.ngOnInit();
    expect(mockSvc.list).toHaveBeenCalled();
    expect(mockStore.loadStaff).toHaveBeenCalled();
  });

  it('isFormValid is false until a staff member and a positive amount are set', () => {
    const c = make();
    expect(c.isFormValid).toBe(false);
    c.form.employeeId = 'e1';
    c.form.amount = 0;
    expect(c.isFormValid).toBe(false);     // zero amount rejected
    c.form.amount = 50;
    expect(c.isFormValid).toBe(true);
  });

  it('save() posts the mapped body and toasts success', () => {
    const c = make();
    c.form = { employeeId: 'e1', amount: 120, advanceDate: new Date(2026, 5, 30), reason: ' rent ' };
    c.save();
    expect(mockSvc.create).toHaveBeenCalledWith({
      employeeId: 'e1', amount: 120, advanceDate: '2026-06-30', reason: 'rent',
    });
    expect(mockToast.success).toHaveBeenCalled();
  });

  it('save() does nothing when the form is invalid', () => {
    const c = make();
    c.form = { employeeId: '', amount: null, advanceDate: new Date(), reason: '' };
    c.save();
    expect(mockSvc.create).not.toHaveBeenCalled();
  });

  it('toggleSettled() settles an outstanding advance', () => {
    const c = make();
    c.toggleSettled({ id: 'a1', status: 'outstanding' } as any);
    expect(mockSvc.setSettled).toHaveBeenCalledWith('a1', true);
  });

  it('toggleSettled() reopens a settled advance', () => {
    const c = make();
    c.toggleSettled({ id: 'a1', status: 'settled' } as any);
    expect(mockSvc.setSettled).toHaveBeenCalledWith('a1', false);
  });

  it('toggleSettled() does NOT change status when the confirm is rejected', () => {
    mockConfirm.confirm.mockReturnValue(of(false));
    const c = make();
    c.toggleSettled({ id: 'a1', status: 'outstanding' } as any);
    expect(mockSvc.setSettled).not.toHaveBeenCalled();
  });

  it('remove() deletes only after the confirm is accepted', () => {
    const c = make();
    c.remove({ id: 'a1' } as any);
    expect(mockSvc.delete).toHaveBeenCalledWith('a1');
  });

  it('remove() does not delete when the confirm is rejected', () => {
    mockConfirm.confirm.mockReturnValue(of(false));
    const c = make();
    c.remove({ id: 'a1' } as any);
    expect(mockSvc.delete).not.toHaveBeenCalled();
  });

  it('totalOutstanding sums only outstanding advances', () => {
    const c = make();
    c.advances.set([
      { status: 'outstanding', amount: 100 } as any,
      { status: 'settled',     amount: 999 } as any,
      { status: 'outstanding', amount: 50 } as any,
    ]);
    expect(c.totalOutstanding()).toBe(150);
  });

  it('shows an error toast when loading fails', () => {
    mockSvc.list.mockReturnValue(throwError(() => new Error('net')));
    const c = make();
    c.load();
    expect(mockToast.error).toHaveBeenCalled();
  });

  // ── Phase 7r: card view ─────────────────────────────────────────────────────

  describe('card view', () => {
    const advance = (n: number) => ({
      id: 'a' + n, employeeId: 'e' + n, staffId: 'S' + n, staffName: 'Person ' + n,
      amount: 100, advanceDate: '2026-07-' + String(n).padStart(2, '0'), reason: null, status: 'outstanding',
    }) as never;

    it('starts on cards', () => {
      expect(make().viewMode()).toBe('cards');
    });

    it('shows the same advances as the table, newest first, a page at a time', () => {
      const c = make();
      c.advances.set(Array.from({ length: 15 }, (_, i) => advance(i + 1)));

      expect(c.pagedAdvances().map(a => a.id)).toEqual(
        Array.from({ length: 12 }, (_, i) => 'a' + (15 - i)));
      c.cardFirst.set(12);
      expect(c.pagedAdvances().map(a => a.id)).toEqual(['a3', 'a2', 'a1']);
    });

    it('goes back to the first page when the list shrinks below the current page', () => {
      const c = make();
      c.advances.set([advance(1), advance(2)]);
      c.cardFirst.set(24);
      expect(c.pagedAdvances().length).toBe(2);
    });
  });

});
