import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { PayrollLockService } from './payroll-lock.service';

describe('PayrollLockService', () => {
  let service: PayrollLockService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PayrollLockService, provideHttpClient(withXhr()), provideHttpClientTesting()],
    });
    service = TestBed.inject(PayrollLockService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() GETs /api/payroll-locks', () => {
    service.list().subscribe();
    const req = http.expectOne('/api/payroll-locks');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('list(year) adds the year query param', () => {
    service.list(2026).subscribe();
    const req = http.expectOne('/api/payroll-locks?year=2026');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('lock() POSTs year/month/note', () => {
    service.lock(2026, 5, 'done').subscribe();
    const req = http.expectOne('/api/payroll-locks');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ year: 2026, month: 5, note: 'done' });
    req.flush({ id: 'x' });
  });

  it('lock() sends null note when omitted', () => {
    service.lock(2026, 5).subscribe();
    const req = http.expectOne('/api/payroll-locks');
    expect(req.request.body).toEqual({ year: 2026, month: 5, note: null });
    req.flush({ id: 'x' });
  });

  it('reopen() DELETEs /api/payroll-locks/{year}/{month}', () => {
    service.reopen(2026, 5).subscribe();
    const req = http.expectOne('/api/payroll-locks/2026/5');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
