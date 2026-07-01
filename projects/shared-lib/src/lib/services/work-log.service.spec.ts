import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { WorkLogService, WorkLogDto } from './work-log.service';

/**
 * WorkLogService — HTTP transport for shift log operations.
 * WHY: ensures each method targets the correct URL, sends the right payload,
 * and passes through the API response without transformation.
 */

function makeDto(overrides: Partial<WorkLogDto> = {}): WorkLogDto {
  return {
    id: 'log-1', roleName: 'BARTENDER', locationId: 'loc-1', locationName: 'Main Bar',
    workDate: '2026-05-17', startTime: '18:00', endDate: '2026-05-17', endTime: '23:00',
    hours: 5, cost: 250, isActive: false, isManualEntry: false, isEdited: false, notes: null,
    ...overrides,
  };
}

describe('WorkLogService', () => {
  let service: WorkLogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [WorkLogService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WorkLogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getActive() ───────────────────────────────────────────────────────────────

  describe('updateActiveLocation()', () => {
    it('PATCHes /api/worklogs/active/location with the fresh coordinates', () => {
      service.updateActiveLocation(3.1390, 101.6869).subscribe();
      const req = http.expectOne('/api/worklogs/active/location');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ latitude: 3.1390, longitude: 101.6869 });
      req.flush(null);
    });
  });

  describe('getActive()', () => {
    it('calls GET /api/worklogs/active', () => {
      service.getActive().subscribe();
      const req = http.expectOne('/api/worklogs/active');
      expect(req.request.method).toBe('GET');
      req.flush(null);
    });

    it('returns null when server returns null — no active shift', () => {
      let result: WorkLogDto | null | undefined;
      service.getActive().subscribe(v => (result = v));
      http.expectOne('/api/worklogs/active').flush(null);
      expect(result).toBeNull();
    });

    it('returns WorkLogDto when an active shift exists', () => {
      const dto = makeDto({ isActive: true, endTime: null });
      let result: WorkLogDto | null | undefined;
      service.getActive().subscribe(v => (result = v));
      http.expectOne('/api/worklogs/active').flush(dto);
      expect(result).toEqual(dto);
    });
  });

  // ── getMy() ───────────────────────────────────────────────────────────────────

  describe('getMy()', () => {
    it('calls GET /api/worklogs/my with default page=1 and pageSize=10', () => {
      service.getMy().subscribe();
      const req = http.expectOne(r => r.urlWithParams.startsWith('/api/worklogs/my'));
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('page=1');
      expect(req.request.urlWithParams).toContain('pageSize=10');
      req.flush({ items: [], total: 0 });
    });

    it('includes the page param in the URL when explicitly provided', () => {
      service.getMy(3).subscribe();
      const req = http.expectOne(r => r.urlWithParams.startsWith('/api/worklogs/my'));
      expect(req.request.urlWithParams).toContain('page=3');
      req.flush({ items: [], total: 0 });
    });

    it('includes the pageSize param in the URL when explicitly provided', () => {
      service.getMy(1, 25).subscribe();
      const req = http.expectOne(r => r.urlWithParams.startsWith('/api/worklogs/my'));
      expect(req.request.urlWithParams).toContain('pageSize=25');
      req.flush({ items: [], total: 0 });
    });

    it('returns a PagedResult<WorkLogDto> with items and total', () => {
      const dto = makeDto();
      let result: any;
      service.getMy().subscribe(v => (result = v));
      http.expectOne(r => r.urlWithParams.startsWith('/api/worklogs/my')).flush({ items: [dto], total: 1 });
      expect(result).toEqual({ items: [dto], total: 1 });
    });
  });

  // ── start() ───────────────────────────────────────────────────────────────────

  describe('start()', () => {
    it('calls POST /api/worklogs/start', () => {
      service.start({ roleId: 'role-bar', locationId: 'loc-1', startTime: '18:00' }).subscribe();
      const req = http.expectOne('/api/worklogs/start');
      expect(req.request.method).toBe('POST');
      req.flush({ id: 'log-new' });
    });

    it('sends the full request body: roleId, locationId, startTime, notes', () => {
      const payload = { roleId: 'role-bar', locationId: 'loc-1', startTime: '18:00', notes: 'Late shift' };
      service.start(payload).subscribe();
      const req = http.expectOne('/api/worklogs/start');
      expect(req.request.body).toEqual(payload);
      req.flush({ id: 'log-new' });
    });

    it('returns {id: string} from the API response', () => {
      let result: { id: string } | undefined;
      service.start({ roleId: 'role-bar', locationId: 'loc-1', startTime: '18:00' }).subscribe(v => (result = v));
      http.expectOne('/api/worklogs/start').flush({ id: 'log-abc' });
      expect(result).toEqual({ id: 'log-abc' });
    });
  });

  // ── createManual() ────────────────────────────────────────────────────────────

  describe('createManual()', () => {
    const manualPayload = {
      roleId: 'role-bar', locationId: 'loc-1',
      workDate: '2026-05-17', startTime: '18:00', endTime: '23:00', notes: null,
    };

    it('calls POST /api/worklogs/entry', () => {
      service.createManual(manualPayload).subscribe();
      const req = http.expectOne('/api/worklogs/entry');
      expect(req.request.method).toBe('POST');
      req.flush({ id: 'log-manual' });
    });

    it('sends a body with all required manual-entry fields', () => {
      service.createManual(manualPayload).subscribe();
      const req = http.expectOne('/api/worklogs/entry');
      expect(req.request.body).toEqual(manualPayload);
      req.flush({ id: 'log-manual' });
    });

    it('returns {id: string} from the API response', () => {
      let result: { id: string } | undefined;
      service.createManual(manualPayload).subscribe(v => (result = v));
      http.expectOne('/api/worklogs/entry').flush({ id: 'log-manual-99' });
      expect(result).toEqual({ id: 'log-manual-99' });
    });
  });

  // ── end() ─────────────────────────────────────────────────────────────────────

  describe('end()', () => {
    it('calls PUT /api/worklogs/{id}/end', () => {
      service.end('log-42', '23:00').subscribe();
      const req = http.expectOne('/api/worklogs/log-42/end');
      expect(req.request.method).toBe('PUT');
      req.flush(null);
    });

    it('sends {endTime} in the request body', () => {
      service.end('log-42', '23:00').subscribe();
      const req = http.expectOne('/api/worklogs/log-42/end');
      expect(req.request.body).toEqual({ endTime: '23:00' });
      req.flush(null);
    });

    it('uses the provided id in the URL — not a hardcoded value', () => {
      service.end('log-xyz', '01:00').subscribe();
      const req = http.expectOne('/api/worklogs/log-xyz/end');
      expect(req.request.url).toContain('log-xyz');
      req.flush(null);
    });
  });

  // ── update() ─────────────────────────────────────────────────────────────────

  describe('update()', () => {
    const updatePayload = {
      roleId: 'role-bar', locationId: 'loc-1',
      workDate: '2026-05-17', startTime: '18:00', endDate: '2026-05-17', endTime: '23:00', notes: 'Edited',
    };

    it('calls PUT /api/worklogs/{id}', () => {
      service.update('log-5', updatePayload).subscribe();
      const req = http.expectOne('/api/worklogs/log-5');
      expect(req.request.method).toBe('PUT');
      req.flush(null);
    });

    it('sends the full update body to the correct resource URL', () => {
      service.update('log-5', updatePayload).subscribe();
      const req = http.expectOne('/api/worklogs/log-5');
      expect(req.request.body).toEqual(updatePayload);
      req.flush(null);
    });
  });

  // ── delete() ─────────────────────────────────────────────────────────────────

  describe('delete()', () => {
    it('calls DELETE /api/worklogs/{id}', () => {
      service.delete('log-99').subscribe();
      const req = http.expectOne('/api/worklogs/log-99');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });

    it('uses the provided id in the URL — not a hardcoded value', () => {
      service.delete('log-abc').subscribe();
      const req = http.expectOne('/api/worklogs/log-abc');
      expect(req.request.url).toContain('log-abc');
      req.flush(null);
    });
  });

  // ── getAll() ─────────────────────────────────────────────────────────────────

  describe('getAll()', () => {
    it('calls GET /api/worklogs with no query params when called without args', () => {
      service.getAll().subscribe();
      const req = http.expectOne(r => r.url === '/api/worklogs');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toBe('/api/worklogs');
      req.flush({ items: [], total: 0 });
    });

    it('includes from param when provided', () => {
      service.getAll({ from: '2026-05-01' }).subscribe();
      const req = http.expectOne(r => r.url === '/api/worklogs');
      expect(req.request.urlWithParams).toContain('from=2026-05-01');
      req.flush({ items: [], total: 0 });
    });

    it('includes to param when provided', () => {
      service.getAll({ to: '2026-05-31' }).subscribe();
      const req = http.expectOne(r => r.url === '/api/worklogs');
      expect(req.request.urlWithParams).toContain('to=2026-05-31');
      req.flush({ items: [], total: 0 });
    });

    it('includes employeeId param when provided', () => {
      service.getAll({ employeeId: 'emp-7' }).subscribe();
      const req = http.expectOne(r => r.url === '/api/worklogs');
      expect(req.request.urlWithParams).toContain('employeeId=emp-7');
      req.flush({ items: [], total: 0 });
    });

    it('returns PagedResult<WorkLogDto> with items and total', () => {
      const dto = makeDto();
      let result: any;
      service.getAll({ from: '2026-05-01', to: '2026-05-31' }).subscribe(v => (result = v));
      http.expectOne(r => r.url === '/api/worklogs').flush({ items: [dto], total: 1 });
      expect(result).toEqual({ items: [dto], total: 1 });
    });
  });
});
