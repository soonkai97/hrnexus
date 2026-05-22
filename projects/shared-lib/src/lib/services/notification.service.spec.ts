import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { NotificationService } from './notification.service';
import { AppNotification } from '../models';

/**
 * NotificationService — signal-based notification store with HTTP persistence.
 * WHY: validates that:
 *  - load() replaces the signal state with API data
 *  - push() prepends new notifications (signal update, no HTTP)
 *  - markReadOptimistic() immediately marks one notification locally
 *  - markRead() calls the correct endpoint (persists to API)
 *  - markAllRead() marks all items in the signal and calls the API
 */
describe('NotificationService', () => {
  let service: NotificationService;
  let http: HttpTestingController;

  function makeNotification(overrides: Partial<AppNotification> = {}): AppNotification {
    return { id: 'n1', title: 'Test', message: 'msg', type: 'announcement',
      isRead: false, relatedId: null, createdAt: '2026-01-01 12:00', ...overrides };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [NotificationService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(NotificationService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── load() ────────────────────────────────────────────────────────────────

  describe('load()', () => {
    it('calls GET /api/notifications', () => {
      service.load().subscribe();
      const req = http.expectOne('/api/notifications');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('sets the notifications signal from the API response', async () => {
      const n = makeNotification({ id: 'n1', title: 'Hi' });
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([n]);
      await p;
      expect(service.notifications()).toEqual([n]);
    });

    it('unreadCount computed signal reflects the loaded notifications', async () => {
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([
        makeNotification({ id: 'n1', isRead: false }),
        makeNotification({ id: 'n2', isRead: true }),
        makeNotification({ id: 'n3', isRead: false }),
      ]);
      await p;
      expect(service.unreadCount()).toBe(2);
    });
  });

  // ── push() ────────────────────────────────────────────────────────────────

  describe('push()', () => {
    it('prepends the new notification to the beginning of the list', async () => {
      // Seed via load
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([makeNotification({ id: 'n1' })]);
      await p;

      service.push(makeNotification({ id: 'n-new', title: 'New!' }));

      const list = service.notifications();
      expect(list[0].id).toBe('n-new');
      expect(list[1].id).toBe('n1');
    });

    it('increments unreadCount when pushed notification is unread', async () => {
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([]);
      await p;

      expect(service.unreadCount()).toBe(0);
      service.push(makeNotification({ id: 'n-new', isRead: false }));
      expect(service.unreadCount()).toBe(1);
    });
  });

  // ── markReadOptimistic() ──────────────────────────────────────────────────

  describe('markReadOptimistic()', () => {
    it('immediately marks the matching notification as read in the signal', async () => {
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([
        makeNotification({ id: 'n1', isRead: false }),
        makeNotification({ id: 'n2', isRead: false }),
      ]);
      await p;

      service.markReadOptimistic('n1');

      const n1 = service.notifications().find(n => n.id === 'n1');
      expect(n1?.isRead).toBe(true);
      // n2 should remain unread
      const n2 = service.notifications().find(n => n.id === 'n2');
      expect(n2?.isRead).toBe(false);
    });

    it('decrements unreadCount after optimistic mark', async () => {
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([makeNotification({ id: 'n1', isRead: false })]);
      await p;

      expect(service.unreadCount()).toBe(1);
      service.markReadOptimistic('n1');
      expect(service.unreadCount()).toBe(0);
    });
  });

  // ── markRead() ────────────────────────────────────────────────────────────

  describe('markRead()', () => {
    it('calls POST /api/notifications/{id}/read with an empty body', () => {
      service.markRead('n-5').subscribe();
      const req = http.expectOne('/api/notifications/n-5/read');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({});
      req.flush(null);
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.markRead('n-abc').subscribe();
      const req = http.expectOne('/api/notifications/n-abc/read');
      expect(req.request.url).toContain('n-abc');
      req.flush(null);
    });
  });

  // ── markAllRead() ─────────────────────────────────────────────────────────

  describe('markAllRead()', () => {
    it('calls POST /api/notifications/read-all', () => {
      service.markAllRead().subscribe();
      const req = http.expectOne('/api/notifications/read-all');
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });

    it('sets all notifications to isRead=true in the signal after API success', async () => {
      const p = firstValueFrom(service.load());
      http.expectOne('/api/notifications').flush([
        makeNotification({ id: 'n1', isRead: false }),
        makeNotification({ id: 'n2', isRead: false }),
      ]);
      await p;

      const markAll = firstValueFrom(service.markAllRead());
      http.expectOne('/api/notifications/read-all').flush(null);
      await markAll;

      expect(service.notifications().every(n => n.isRead)).toBe(true);
      expect(service.unreadCount()).toBe(0);
    });
  });
});
