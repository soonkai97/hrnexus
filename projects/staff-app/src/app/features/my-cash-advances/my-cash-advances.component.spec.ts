import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService } from 'primeng/api';
import { CashAdvanceService, ToastService } from 'shared-lib';
import { MyCashAdvancesComponent } from './my-cash-advances.component';

describe('MyCashAdvancesComponent', () => {
  let mockSvc: any;
  let mockToast: any;

  function make(): MyCashAdvancesComponent {
    return TestBed.createComponent(MyCashAdvancesComponent).componentInstance;
  }

  beforeEach(async () => {
    mockSvc   = { listMine: vi.fn().mockReturnValue(of([])) };
    mockToast = { error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [MyCashAdvancesComponent],
      providers: [
        provideHttpClient(),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: CashAdvanceService, useValue: mockSvc },
        { provide: ToastService,       useValue: mockToast },
        MessageService,
      ],
    }).compileComponents();
  });

  it('loads the caller-scoped advances on init', () => {
    const c = make();
    c.ngOnInit();
    expect(mockSvc.listMine).toHaveBeenCalled();
  });

  it('totalOutstanding sums only outstanding advances', () => {
    const c = make();
    c.advances.set([
      { status: 'outstanding', amount: 100 } as any,
      { status: 'settled',     amount: 50 } as any,
    ]);
    expect(c.totalOutstanding()).toBe(100);
  });

  it('shows an error toast when loading fails', () => {
    mockSvc.listMine.mockReturnValue(throwError(() => new Error('net')));
    const c = make();
    c.load();
    expect(mockToast.error).toHaveBeenCalled();
  });
});
