import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService } from 'primeng/api';
import { PayslipDialogComponent } from './payslip-dialog.component';
import { AdminService } from 'shared-lib';

/**
 * PayslipDialogComponent — the reusable payslip drill-down used by Payroll + Staff Detail.
 *
 * WHY these tests matter:
 *  - open() must default the range and immediately load — a wrong default shows the wrong period.
 *  - Dates must be sent LOCAL-formatted (no toISOString UTC shift) or a shift can land in the wrong day.
 *  - An inverted range must NOT call the API; loading/exporting flags must always clear.
 */

const fakePayslip = {
  staffId: 'NC001', staffName: 'Jane Doe', from: '2026-05-01', to: '2026-05-31',
  totalHours: 12, totalPay: 740, outstandingAdvance: 200, netPayable: 540,
  jobBreakdown: [], shifts: [],
};

describe('PayslipDialogComponent', () => {
  let component: PayslipDialogComponent;
  let mockAdminSvc: { getPayslip: ReturnType<typeof vi.fn>; exportPayslipCsv: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAdminSvc = {
      getPayslip:       vi.fn().mockReturnValue(of(fakePayslip)),
      exportPayslipCsv: vi.fn().mockReturnValue(of(new Blob(['csv'], { type: 'text/csv' }))),
    };

    await TestBed.configureTestingModule({
      imports: [PayslipDialogComponent],
      providers: [
        provideHttpClient(),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AdminService, useValue: mockAdminSvc },
        MessageService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(PayslipDialogComponent).componentInstance;
  });

  afterEach(() => { vi.restoreAllMocks(); vi.useRealTimers(); });

  it('open() shows the dialog and loads the explicit range', () => {
    component.open('NC001', 'Jane Doe', new Date(2026, 4, 1), new Date(2026, 4, 31));
    expect(component.visible()).toBe(true);
    expect(component.staffId()).toBe('NC001');
    expect(component.name()).toBe('Jane Doe');
    expect(mockAdminSvc.getPayslip).toHaveBeenCalledWith('NC001', '2026-05-01', '2026-05-31');
    expect(component.payslip()).toEqual(fakePayslip);
  });

  it('open() without a range defaults to the current calendar month', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 6, 15));   // 15 Jul 2026
    component.open('NC001', 'Jane Doe');
    expect(mockAdminSvc.getPayslip).toHaveBeenCalledWith('NC001', '2026-07-01', '2026-07-31');
  });

  it('load() sends LOCAL-formatted dates (no UTC off-by-one)', () => {
    component.staffId.set('NC001');
    component.from.set(new Date(2026, 0, 1));   // 1 Jan 2026 local
    component.to.set(new Date(2026, 0, 31));
    component.load();
    expect(mockAdminSvc.getPayslip).toHaveBeenCalledWith('NC001', '2026-01-01', '2026-01-31');
  });

  it('load() refuses an inverted range (to before from)', () => {
    component.staffId.set('NC001');
    component.from.set(new Date(2026, 4, 31));
    component.to.set(new Date(2026, 4, 1));
    mockAdminSvc.getPayslip.mockClear();
    component.load();
    expect(mockAdminSvc.getPayslip).not.toHaveBeenCalled();
  });

  it('clears the loading flag even when the API call fails', () => {
    mockAdminSvc.getPayslip.mockReturnValue(throwError(() => new Error('Network')));
    component.staffId.set('NC001');
    component.from.set(new Date(2026, 4, 1));
    component.to.set(new Date(2026, 4, 31));
    component.load();
    expect(component.loading()).toBe(false);
  });

  it('exportCsv() requests the CSV for the current range and clears the flag', () => {
    vi.spyOn(document, 'createElement').mockReturnValue(
      Object.assign(document.createElement('a'), { click: vi.fn() }),
    );
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fake');
    vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);

    component.staffId.set('NC001');
    component.from.set(new Date(2026, 4, 1));
    component.to.set(new Date(2026, 4, 31));
    component.exportCsv();
    expect(mockAdminSvc.exportPayslipCsv).toHaveBeenCalledWith('NC001', '2026-05-01', '2026-05-31');
    expect(component.exporting()).toBe(false);
  });
});
