import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { PayrollRatesService } from './payroll-rates.service';

describe('PayrollRatesService', () => {
  let service: PayrollRatesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PayrollRatesService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PayrollRatesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() GETs /api/payroll/rates and returns the rows', () => {
    let rows: unknown[] = [];
    service.list().subscribe((r) => (rows = r));
    const req = http.expectOne('/api/payroll/rates');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'r1' }]);
    expect(rows).toHaveLength(1);
  });

  it('verify() POSTs the note to /api/payroll/rates/{id}/verify', () => {
    service.verify('r1', 'Checked against the KWSP table').subscribe();
    const req = http.expectOne('/api/payroll/rates/r1/verify');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ note: 'Checked against the KWSP table' });
    req.flush(null);
  });

  it('setActive() POSTs the flag to /api/payroll/rates/{id}/active', () => {
    service.setActive('r2', true).subscribe();
    const req = http.expectOne('/api/payroll/rates/r2/active');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ active: true });
    req.flush(null);
  });
});
