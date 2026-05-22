import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PayrollService } from './payroll.service';

/**
 * PayrollService — HTTP transport for employee pay summary.
 * WHY: ensures getMySummary() reaches the right URL with both period params
 * correctly encoded — a wrong endpoint or missing param yields wrong data silently.
 */
describe('PayrollService', () => {
  let service: PayrollService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PayrollService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PayrollService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getMySummary() ────────────────────────────────────────────────────────

  describe('getMySummary()', () => {
    it('calls GET /api/worklogs/pay-summary with start and end params', () => {
      service.getMySummary('2026-05-01', '2026-05-31').subscribe();
      const req = http.expectOne(r => r.url.includes('/api/worklogs/pay-summary'));
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('start=2026-05-01');
      expect(req.request.urlWithParams).toContain('end=2026-05-31');
      req.flush({});
    });

    it('uses the provided periodStart in the URL — not hardcoded', () => {
      service.getMySummary('2026-01-01', '2026-01-31').subscribe();
      const req = http.expectOne(r => r.url.includes('/api/worklogs/pay-summary'));
      expect(req.request.urlWithParams).toContain('start=2026-01-01');
      req.flush({});
    });

    it('uses the provided periodEnd in the URL — not hardcoded', () => {
      service.getMySummary('2026-01-01', '2026-12-31').subscribe();
      const req = http.expectOne(r => r.url.includes('/api/worklogs/pay-summary'));
      expect(req.request.urlWithParams).toContain('end=2026-12-31');
      req.flush({});
    });

    it('passes through the API response without transformation', () => {
      const summary = { shifts: 10, regularHours: 80, overtimeHours: 5, totalPay: 2000 };
      let result: any;
      service.getMySummary('2026-05-01', '2026-05-31').subscribe(v => (result = v));
      http.expectOne(r => r.url.includes('/api/worklogs/pay-summary')).flush(summary);
      expect(result).toEqual(summary);
    });
  });
});
