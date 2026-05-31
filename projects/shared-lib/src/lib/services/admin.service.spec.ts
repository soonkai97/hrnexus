import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AdminService } from './admin.service';

/**
 * AdminService — HTTP transport for admin-facing data.
 * WHY: ensures each method hits the correct URL, sends the right params/body,
 * and passes through API responses without transformation.
 */
describe('AdminService', () => {
  let service: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getDashboard() ────────────────────────────────────────────────────────

  describe('getDashboard()', () => {
    it('calls GET /api/admin/dashboard', () => {
      service.getDashboard().subscribe();
      const req = http.expectOne('/api/admin/dashboard');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });
  });

  // ── getReport() ───────────────────────────────────────────────────────────

  describe('getReport()', () => {
    const baseFilters = { dateFrom: '2026-01-01', dateTo: '2026-01-31' };

    it('calls GET /api/admin/report with dateFrom and dateTo', () => {
      service.getReport(baseFilters).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/report');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('dateFrom=2026-01-01');
      expect(req.request.urlWithParams).toContain('dateTo=2026-01-31');
      req.flush({});
    });

    it('appends staffIds as repeated params when provided', () => {
      service.getReport({ ...baseFilters, staffIds: ['id-1', 'id-2'] }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/report');
      expect(req.request.urlWithParams).toContain('staffIds=id-1');
      expect(req.request.urlWithParams).toContain('staffIds=id-2');
      req.flush({});
    });

    it('appends locationNames as repeated params when provided', () => {
      service.getReport({ ...baseFilters, locationNames: ['Main Bar'] }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/report');
      expect(req.request.urlWithParams).toContain('locationNames=Main%20Bar');
      req.flush({});
    });

    it('appends roles as repeated params when provided', () => {
      service.getReport({ ...baseFilters, roles: ['BARTENDER'] }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/report');
      expect(req.request.urlWithParams).toContain('roles=BARTENDER');
      req.flush({});
    });
  });

  // ── exportPayrollCsv() ────────────────────────────────────────────────────

  describe('exportPayrollCsv()', () => {
    it('calls GET /api/admin/payroll/export with year and month params', () => {
      service.exportPayrollCsv(2026, 5).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/payroll/export');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('year=2026');
      expect(req.request.urlWithParams).toContain('month=5');
      req.flush(new Blob());
    });

    it('requests a blob response type for binary download', () => {
      service.exportPayrollCsv(2026, 1).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/payroll/export');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob());
    });
  });

  // ── getPayrollSummary() ───────────────────────────────────────────────────

  describe('getPayrollSummary()', () => {
    it('calls GET /api/admin/payroll/summary with year and month params', () => {
      service.getPayrollSummary(2026, 5).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/payroll/summary');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('year=2026');
      expect(req.request.urlWithParams).toContain('month=5');
      req.flush({ year: 2026, month: 5, staff: [], totalGross: 0 });
    });
  });
});
