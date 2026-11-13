import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { StaffManagementService } from './staff-management.service';

/**
 * StaffManagementService — HTTP transport for employee CRUD and roles.
 * WHY: ensures each method hits the correct URL and sends the right params/body
 * so that admin operations always reach their intended backend endpoints.
 */
describe('StaffManagementService', () => {
  let service: StaffManagementService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [StaffManagementService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(StaffManagementService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getAll() ──────────────────────────────────────────────────────────────

  describe('getAll()', () => {
    it('calls GET /api/staff with default page=1 and pageSize=10', () => {
      service.getAll().subscribe();
      const req = http.expectOne(r => r.url === '/api/staff');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('page=1');
      expect(req.request.urlWithParams).toContain('pageSize=10');
      req.flush({ items: [], total: 0 });
    });

    it('includes search param when provided', () => {
      service.getAll('alice').subscribe();
      const req = http.expectOne(r => r.url === '/api/staff');
      expect(req.request.urlWithParams).toContain('search=alice');
      req.flush({ items: [], total: 0 });
    });

    it('includes custom page and pageSize when provided', () => {
      service.getAll(undefined, 3, 20).subscribe();
      const req = http.expectOne(r => r.url === '/api/staff');
      expect(req.request.urlWithParams).toContain('page=3');
      expect(req.request.urlWithParams).toContain('pageSize=20');
      req.flush({ items: [], total: 0 });
    });

    it('does not include search param when not provided', () => {
      service.getAll().subscribe();
      const req = http.expectOne(r => r.url === '/api/staff');
      expect(req.request.urlWithParams).not.toContain('search=');
      req.flush({ items: [], total: 0 });
    });

    it('does not include includeHidden by default', () => {
      service.getAll().subscribe();
      const req = http.expectOne(r => r.url === '/api/staff');
      expect(req.request.urlWithParams).not.toContain('includeHidden');
      req.flush({ items: [], total: 0 });
    });

    it('includes includeHidden=true when requested', () => {
      service.getAll(undefined, 1, 10, true).subscribe();
      const req = http.expectOne(r => r.url === '/api/staff');
      expect(req.request.urlWithParams).toContain('includeHidden=true');
      req.flush({ items: [], total: 0 });
    });
  });

  // ── setHidden() ─────────────────────────────────────────────────────────────

  describe('setHidden()', () => {
    it('PATCHes /api/staff/{id}/hidden with the hidden flag', () => {
      service.setHidden('emp-9', true).subscribe();
      const req = http.expectOne('/api/staff/emp-9/hidden');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ hidden: true });
      req.flush(null);
    });
  });

  // ── getById() ─────────────────────────────────────────────────────────────

  describe('getById()', () => {
    it('calls GET /api/staff/{id}', () => {
      service.getById('emp-5').subscribe();
      const req = http.expectOne('/api/staff/emp-5');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.getById('emp-xyz').subscribe();
      const req = http.expectOne('/api/staff/emp-xyz');
      expect(req.request.url).toContain('emp-xyz');
      req.flush({});
    });
  });

  // ── register() ────────────────────────────────────────────────────────────

  describe('register()', () => {
    it('calls POST /api/staff with the registration body', () => {
      const req: any = { staffId: 'S001', firstName: 'Alice', lastName: 'Wong',
        email: 'a@test.com', password: 'pass1234', roles: ['BARTENDER'] };
      service.register(req).subscribe();
      const httpReq = http.expectOne('/api/staff');
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toEqual(req);
      httpReq.flush({ id: 'new-id' });
    });
  });

  // ── update() ──────────────────────────────────────────────────────────────

  describe('update()', () => {
    it('calls PUT /api/staff/{id} with the update body', () => {
      const req: any = { firstName: 'Bob', roles: ['MANAGER'] };
      service.update('emp-3', req).subscribe();
      const httpReq = http.expectOne('/api/staff/emp-3');
      expect(httpReq.request.method).toBe('PUT');
      expect(httpReq.request.body).toEqual(req);
      httpReq.flush(null);
    });
  });

  // ── toggleActive() ────────────────────────────────────────────────────────

  describe('toggleActive()', () => {
    it('calls PATCH /api/staff/{id}/activate when activate=true', () => {
      service.toggleActive('emp-1', true).subscribe();
      const req = http.expectOne('/api/staff/emp-1/activate');
      expect(req.request.method).toBe('PATCH');
      req.flush(null);
    });

    it('calls PATCH /api/staff/{id}/deactivate when activate=false', () => {
      service.toggleActive('emp-2', false).subscribe();
      const req = http.expectOne('/api/staff/emp-2/deactivate');
      expect(req.request.method).toBe('PATCH');
      req.flush(null);
    });
  });

  // ── getRoles() ────────────────────────────────────────────────────────────

  describe('getRoles()', () => {
    it('calls GET /api/staff/roles', () => {
      service.getRoles().subscribe();
      const req = http.expectOne('/api/staff/roles');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  // ── createRole() ──────────────────────────────────────────────────────────

  describe('createRole()', () => {
    it('calls POST /api/staff/roles with name and hourlyRate', () => {
      service.createRole('BARTENDER', 25).subscribe();
      const req = http.expectOne('/api/staff/roles');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ name: 'BARTENDER', hourlyRate: 25, nameZh: null, nameVi: null, payType: 'hourly' });
      req.flush(null);
    });

    it('defaults hourlyRate to 0 when not provided', () => {
      service.createRole('DOORMAN').subscribe();
      const req = http.expectOne('/api/staff/roles');
      expect(req.request.body).toEqual({ name: 'DOORMAN', hourlyRate: 0, nameZh: null, nameVi: null, payType: 'hourly' });
      req.flush(null);
    });

    it('sends payType "fixed" for a flat-per-shift job', () => {
      service.createRole('PACKAGE', 500, null, null, 'fixed').subscribe();
      const req = http.expectOne('/api/staff/roles');
      expect(req.request.body).toEqual({ name: 'PACKAGE', hourlyRate: 500, nameZh: null, nameVi: null, payType: 'fixed' });
      req.flush(null);
    });
  });

  // ── updateRoleRate() ──────────────────────────────────────────────────────

  describe('updateRoleRate()', () => {
    it('calls PATCH /api/staff/roles/{id}/rate with hourlyRate body', () => {
      service.updateRoleRate('role-guid-1', 30).subscribe();
      const req = http.expectOne('/api/staff/roles/role-guid-1/rate');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ hourlyRate: 30, nameZh: null, nameVi: null, payType: 'hourly', name: null });
      req.flush(null);
    });
  });

  // ── deleteRole() ──────────────────────────────────────────────────────────

  describe('deleteRole()', () => {
    it('calls DELETE /api/staff/roles/{id}', () => {
      service.deleteRole('role-guid-1').subscribe();
      const req = http.expectOne('/api/staff/roles/role-guid-1');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ── resetPassword() ───────────────────────────────────────────────────────

  describe('resetPassword()', () => {
    it('calls POST /api/staff/{id}/reset-password with newPassword body', () => {
      service.resetPassword('emp-5', 'newpass123').subscribe();
      const req = http.expectOne('/api/staff/emp-5/reset-password');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ newPassword: 'newpass123' });
      req.flush(null);
    });
  });

  // ── delete() ──────────────────────────────────────────────────────────────

  describe('delete()', () => {
    it('calls DELETE /api/staff/{id}', () => {
      service.delete('emp-99').subscribe();
      const req = http.expectOne('/api/staff/emp-99');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ── getMyProfile() ────────────────────────────────────────────────────────

  describe('getMyProfile()', () => {
    it('calls GET /api/auth/me', () => {
      service.getMyProfile().subscribe();
      const req = http.expectOne('/api/auth/me');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });
  });

  // ── bulk actions ──────────────────────────────────────────────────────────

  describe('bulkAssignRoles()', () => {
    it('POSTs /api/staff/bulk/roles with staffIds and roles', () => {
      service.bulkAssignRoles(['a'], ['BARTENDER', 'STAFF']).subscribe();
      const req = http.expectOne('/api/staff/bulk/roles');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ staffIds: ['a'], roles: ['BARTENDER', 'STAFF'] });
      req.flush({ count: 1 });
    });
  });

  describe('bulkDeactivate()', () => {
    it('POSTs /api/staff/bulk/deactivate with staffIds', () => {
      service.bulkDeactivate(['a', 'b', 'c']).subscribe();
      const req = http.expectOne('/api/staff/bulk/deactivate');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ staffIds: ['a', 'b', 'c'] });
      req.flush({ count: 3 });
    });
  });

});
