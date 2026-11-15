import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PayrollService } from './payroll.service';

/**
 * PayrollService — salary profile + employment details for one staff member.
 * WHY: pay data is sensitive and append-only on the server; a wrong URL/verb/body would
 * write pay to the wrong person or overwrite history, so each call is pinned here.
 */
describe('PayrollService', () => {
  let service: PayrollService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PayrollService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PayrollService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getSalary() GETs the overview for that staff id', () => {
    const overview = {
      current: null,
      history: [],
      employment: { citizenshipStatus: null, startDate: null, endDate: null },
    };
    service.getSalary('s1').subscribe((r) => expect(r).toEqual(overview));
    const req = http.expectOne('/api/staff/s1/salary');
    expect(req.request.method).toBe('GET');
    req.flush(overview);
  });

  it('addSalary() POSTs the body to the staff salary endpoint', () => {
    const body = {
      basicSalary: 3200.5,
      payFrequency: 'monthly' as const,
      effectiveFrom: '2026-12-01',
      bankName: 'Maybank',
      bankAccount: '1234567890',
      bankHolder: 'Jane',
    };
    service.addSalary('s1', body).subscribe((r) => expect(r).toEqual({ id: 'p1' }));
    const req = http.expectOne('/api/staff/s1/salary');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ id: 'p1' }, { status: 201, statusText: 'Created' });
  });

  it('setEmployment() PUTs the details to the employment endpoint', () => {
    const body = { citizenshipStatus: 'citizen' as const, startDate: '2026-01-01', endDate: null };
    service.setEmployment('s1', body).subscribe();
    const req = http.expectOne('/api/staff/s1/employment');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('url-encodes the staff id', () => {
    service.getSalary('a/b').subscribe();
    http.expectOne('/api/staff/a%2Fb/salary').flush({});
  });

  it('getPayItems() GETs the items of that person', () => {
    service.getPayItems('s1').subscribe();
    const req = http.expectOne('/api/staff/s1/pay-items');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('addPayItem() POSTs the item', () => {
    const body = { kind: 'allowance' as const, reason: 'Transport', amount: 150, countsForEpf: true, countsForSocsoEis: true, priority: 0, from: '2026-01-01', to: null };
    service.addPayItem('s1', body).subscribe();
    const req = http.expectOne('/api/staff/s1/pay-items');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ id: 'i1' });
  });

  it('endPayItem() PUTs the end date and deletePayItem() DELETEs the item', () => {
    service.endPayItem('s1', 'i1', '2026-12-31').subscribe();
    const end = http.expectOne('/api/staff/s1/pay-items/i1/end');
    expect(end.request.method).toBe('PUT');
    expect(end.request.body).toEqual({ to: '2026-12-31' });
    end.flush(null);

    service.deletePayItem('s1', 'i1').subscribe();
    const del = http.expectOne('/api/staff/s1/pay-items/i1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
  });
});
