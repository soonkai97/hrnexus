import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { CashAdvanceService } from './cash-advance.service';

describe('CashAdvanceService', () => {
  let service: CashAdvanceService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CashAdvanceService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CashAdvanceService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() GETs all advances when no status filter', () => {
    service.list().subscribe();
    const req = http.expectOne('/api/cash-advances');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('list(status) GETs with the status query param', () => {
    service.list('outstanding').subscribe();
    http.expectOne('/api/cash-advances?status=outstanding').flush([]);
  });

  it('create() POSTs the advance body', () => {
    service.create({ employeeId: 'e1', amount: 100, advanceDate: '2026-06-30', reason: null }).subscribe();
    const req = http.expectOne('/api/cash-advances');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ employeeId: 'e1', amount: 100, advanceDate: '2026-06-30', reason: null });
    req.flush({ id: 'a1' });
  });

  it('setSettled() PATCHes the settled flag', () => {
    service.setSettled('a1', true).subscribe();
    const req = http.expectOne('/api/cash-advances/a1/settled');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ settled: true });
    req.flush(null);
  });

  it('delete() DELETEs the advance', () => {
    service.delete('a1').subscribe();
    const req = http.expectOne('/api/cash-advances/a1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
