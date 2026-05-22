import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AnnouncementService, UpsertAnnouncementDto } from './announcement.service';

/**
 * AnnouncementService — HTTP transport for announcement CRUD.
 * WHY: ensures all methods use the correct URLs and HTTP methods so that
 * both the staff view (getMine/markRead) and admin view (getAll/create/update/delete)
 * reach the right backend endpoints.
 */
describe('AnnouncementService', () => {
  let service: AnnouncementService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AnnouncementService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AnnouncementService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const upsertDto: UpsertAnnouncementDto = {
    title: 'Staff Meeting', body: 'Tomorrow at 8pm.', targetRoles: '*', expiresAt: null,
  };

  // ── getMine() ─────────────────────────────────────────────────────────────

  describe('getMine()', () => {
    it('calls GET /api/announcements', () => {
      service.getMine().subscribe();
      const req = http.expectOne('/api/announcements');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('returns the array from the API response', () => {
      const item = { id: 'a1', title: 'T', body: 'B', createdByName: 'Admin',
        createdAt: '2026-01-01', expiresAt: null, isRead: false };
      let result: any;
      service.getMine().subscribe(v => (result = v));
      http.expectOne('/api/announcements').flush([item]);
      expect(result).toEqual([item]);
    });
  });

  // ── markRead() ────────────────────────────────────────────────────────────

  describe('markRead()', () => {
    it('calls POST /api/announcements/{id}/read with an empty body', () => {
      service.markRead('ann-5').subscribe();
      const req = http.expectOne('/api/announcements/ann-5/read');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({});
      req.flush(null);
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.markRead('ann-xyz').subscribe();
      const req = http.expectOne('/api/announcements/ann-xyz/read');
      expect(req.request.url).toContain('ann-xyz');
      req.flush(null);
    });
  });

  // ── getAll() ──────────────────────────────────────────────────────────────

  describe('getAll()', () => {
    it('calls GET /api/announcements/all with default page=1 and pageSize=10', () => {
      service.getAll().subscribe();
      const req = http.expectOne(r => r.url === '/api/announcements/all');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('page=1');
      expect(req.request.urlWithParams).toContain('pageSize=10');
      req.flush({ items: [], total: 0 });
    });

    it('includes custom page and pageSize when provided', () => {
      service.getAll(2, 25).subscribe();
      const req = http.expectOne(r => r.url === '/api/announcements/all');
      expect(req.request.urlWithParams).toContain('page=2');
      expect(req.request.urlWithParams).toContain('pageSize=25');
      req.flush({ items: [], total: 0 });
    });
  });

  // ── create() ──────────────────────────────────────────────────────────────

  describe('create()', () => {
    it('calls POST /api/announcements with the dto body', () => {
      service.create(upsertDto).subscribe();
      const req = http.expectOne('/api/announcements');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(upsertDto);
      req.flush({ id: 'new-ann' });
    });

    it('returns {id} from the API response', () => {
      let result: { id: string } | undefined;
      service.create(upsertDto).subscribe(v => (result = v));
      http.expectOne('/api/announcements').flush({ id: 'created-id' });
      expect(result).toEqual({ id: 'created-id' });
    });
  });

  // ── update() ──────────────────────────────────────────────────────────────

  describe('update()', () => {
    it('calls PUT /api/announcements/{id} with the dto body', () => {
      service.update('ann-10', upsertDto).subscribe();
      const req = http.expectOne('/api/announcements/ann-10');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(upsertDto);
      req.flush(null);
    });
  });

  // ── delete() ──────────────────────────────────────────────────────────────

  describe('delete()', () => {
    it('calls DELETE /api/announcements/{id}', () => {
      service.delete('ann-99').subscribe();
      const req = http.expectOne('/api/announcements/ann-99');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.delete('ann-abc').subscribe();
      const req = http.expectOne('/api/announcements/ann-abc');
      expect(req.request.url).toContain('ann-abc');
      req.flush(null);
    });
  });
});
