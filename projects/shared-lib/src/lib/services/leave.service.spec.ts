import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { LeaveService, ApplyLeaveRequest } from './leave.service';

/**
 * LeaveService — HTTP transport for leave request and balance operations.
 * WHY: guards that both admin endpoints (/api/admin/leave) and staff endpoints
 * (/api/leave) are hit correctly, with all filter params forwarded.
 */
describe('LeaveService', () => {
  let service: LeaveService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LeaveService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LeaveService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getAll() ──────────────────────────────────────────────────────────────

  describe('getAll()', () => {
    it('calls GET /api/admin/leave with no params when called without args', () => {
      service.getAll().subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/leave');
      expect(req.request.method).toBe('GET');
      req.flush({ items: [], total: 0 });
    });

    it('includes employeeId param when provided', () => {
      service.getAll({ employeeId: 'emp-1' }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/leave');
      expect(req.request.urlWithParams).toContain('employeeId=emp-1');
      req.flush({ items: [], total: 0 });
    });

    it('includes status param when provided', () => {
      service.getAll({ status: 'pending' }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/leave');
      expect(req.request.urlWithParams).toContain('status=pending');
      req.flush({ items: [], total: 0 });
    });

    it('includes dateFrom and dateTo when provided', () => {
      service.getAll({ dateFrom: '2026-01-01', dateTo: '2026-01-31' }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/leave');
      expect(req.request.urlWithParams).toContain('dateFrom=2026-01-01');
      expect(req.request.urlWithParams).toContain('dateTo=2026-01-31');
      req.flush({ items: [], total: 0 });
    });

    it('includes page and pageSize params when provided', () => {
      service.getAll({ page: 2, pageSize: 25 }).subscribe();
      const req = http.expectOne(r => r.url === '/api/admin/leave');
      expect(req.request.urlWithParams).toContain('page=2');
      expect(req.request.urlWithParams).toContain('pageSize=25');
      req.flush({ items: [], total: 0 });
    });
  });

  // ── create() (admin) ──────────────────────────────────────────────────────

  describe('create()', () => {
    it('calls POST /api/admin/leave with the request body', () => {
      const req = { employeeId: 'emp-1', leaveType: 'annual', startDate: '2026-06-01', endDate: '2026-06-03', status: 'pending' } as any;
      service.create(req).subscribe();
      const httpReq = http.expectOne('/api/admin/leave');
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toEqual(req);
      httpReq.flush({ id: 'leave-new' });
    });
  });

  // ── update() (admin) ──────────────────────────────────────────────────────

  describe('update()', () => {
    it('calls PUT /api/admin/leave/{id} with the request body', () => {
      const req = { status: 'approved' } as any;
      service.update('leave-5', req).subscribe();
      const httpReq = http.expectOne('/api/admin/leave/leave-5');
      expect(httpReq.request.method).toBe('PUT');
      expect(httpReq.request.body).toEqual(req);
      httpReq.flush(null);
    });
  });

  // ── delete() (admin) ──────────────────────────────────────────────────────

  describe('delete()', () => {
    it('calls DELETE /api/admin/leave/{id}', () => {
      service.delete('leave-99').subscribe();
      const req = http.expectOne('/api/admin/leave/leave-99');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ── getMine() ─────────────────────────────────────────────────────────────

  describe('getMine()', () => {
    it('calls GET /api/leave/mine', () => {
      service.getMine().subscribe();
      const req = http.expectOne('/api/leave/mine');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  // ── apply() ───────────────────────────────────────────────────────────────

  describe('apply()', () => {
    it('calls POST /api/leave with the apply request', () => {
      const applyReq: ApplyLeaveRequest = {
        leaveType: 'sick', startDate: '2026-06-05', endDate: '2026-06-05',
        startTime: null, endTime: null, hours: null, notes: 'Unwell',
      };
      service.apply(applyReq).subscribe();
      const req = http.expectOne('/api/leave');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(applyReq);
      req.flush({ id: 'leave-new' });
    });
  });

  // ── cancel() ──────────────────────────────────────────────────────────────

  describe('cancel()', () => {
    it('calls DELETE /api/leave/{id}', () => {
      service.cancel('leave-7').subscribe();
      const req = http.expectOne('/api/leave/leave-7');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ── getMyBalance() ────────────────────────────────────────────────────────

  describe('getMyBalance()', () => {
    it('calls GET /api/leave/balance with no year param by default', () => {
      service.getMyBalance().subscribe();
      const req = http.expectOne('/api/leave/balance');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });

    it('appends year param when provided', () => {
      service.getMyBalance(2026).subscribe();
      const req = http.expectOne('/api/leave/balance?year=2026');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });
  });

  // ── getAllBalances() ───────────────────────────────────────────────────────

  describe('getAllBalances()', () => {
    it('calls GET /api/admin/leave-balance with no year param by default', () => {
      service.getAllBalances().subscribe();
      const req = http.expectOne('/api/admin/leave-balance');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('appends year param when provided', () => {
      service.getAllBalances(2026).subscribe();
      const req = http.expectOne('/api/admin/leave-balance?year=2026');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  // ── getBalance() ──────────────────────────────────────────────────────────

  describe('getBalance()', () => {
    it('calls GET /api/admin/leave-balance/{employeeId}', () => {
      service.getBalance('emp-5').subscribe();
      const req = http.expectOne('/api/admin/leave-balance/emp-5');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });

    it('appends year param when provided', () => {
      service.getBalance('emp-5', 2026).subscribe();
      const req = http.expectOne('/api/admin/leave-balance/emp-5?year=2026');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });
  });

  // ── setEntitlement() ──────────────────────────────────────────────────────

  describe('setEntitlement()', () => {
    it('calls PUT /api/admin/leave-balance/{employeeId} with year, annualHours, sickHours', () => {
      service.setEntitlement('emp-3', 2026, 112, 84).subscribe();
      const req = http.expectOne('/api/admin/leave-balance/emp-3');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ year: 2026, annualHours: 112, sickHours: 84 });
      req.flush(null);
    });
  });

  // ── bulkMarkNoShow() ──────────────────────────────────────────────────────

  describe('bulkMarkNoShow()', () => {
    it('POSTs /api/admin/leave/bulk-no-show with the id list', () => {
      service.bulkMarkNoShow(['l1', 'l2']).subscribe();
      const req = http.expectOne('/api/admin/leave/bulk-no-show');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ ids: ['l1', 'l2'] });
      req.flush({ count: 2 });
    });
  });
});
