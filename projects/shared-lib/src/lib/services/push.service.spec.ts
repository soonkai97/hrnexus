import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { PushService } from './push.service';

/**
 * PushService — HTTP transport for Web Push subscription management.
 *
 * WHY these tests matter:
 *  - getPublicKey() must hit the correct VAPID endpoint so the browser can
 *    encrypt push payloads; a wrong URL means subscriptions silently fail.
 *  - subscribe() must POST the full subscription object (endpoint + keys) so
 *    the server can deliver push messages; missing fields cause 400 errors.
 *  - unsubscribe() must DELETE with the endpoint in the body so the server
 *    can revoke the subscription and stop delivering messages.
 *  - requestPermission() depends on browser APIs (navigator.serviceWorker /
 *    PushManager) that are not available in jsdom; those paths are tested
 *    via mocks for the guard clauses.
 */
describe('PushService', () => {
  let service: PushService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PushService, provideHttpClient(withXhr()), provideHttpClientTesting()],
    });
    service = TestBed.inject(PushService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getPublicKey() ────────────────────────────────────────────────────────

  describe('getPublicKey()', () => {
    it('calls GET /api/push/vapid-public-key', () => {
      service.getPublicKey().subscribe();
      const req = http.expectOne('/api/push/vapid-public-key');
      expect(req.request.method).toBe('GET');
      req.flush({ publicKey: 'test-key' });
    });

    it('emits the publicKey returned by the API', () => {
      let result: { publicKey: string } | undefined;
      service.getPublicKey().subscribe(r => (result = r));
      http.expectOne('/api/push/vapid-public-key').flush({ publicKey: 'BNcRdreA' });
      expect(result?.publicKey).toBe('BNcRdreA');
    });
  });

  // ── subscribe() ───────────────────────────────────────────────────────────

  describe('subscribe()', () => {
    it('calls POST /api/push/subscribe with the full subscription body', () => {
      const sub = { endpoint: 'https://fcm.example/push/abc', p256dh: 'key1', auth: 'auth1' };
      service.subscribe(sub).subscribe();
      const req = http.expectOne('/api/push/subscribe');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(sub);
      req.flush(null);
    });

    it('sends endpoint, p256dh and auth — all three fields are required for encryption', () => {
      const sub = { endpoint: 'https://example.com/push', p256dh: 'dh-key', auth: 'auth-secret' };
      service.subscribe(sub).subscribe();
      const req = http.expectOne('/api/push/subscribe');
      expect(req.request.body.endpoint).toBe(sub.endpoint);
      expect(req.request.body.p256dh).toBe(sub.p256dh);
      expect(req.request.body.auth).toBe(sub.auth);
      req.flush(null);
    });
  });

  // ── unsubscribe() ─────────────────────────────────────────────────────────

  describe('unsubscribe()', () => {
    it('calls DELETE /api/push/unsubscribe with endpoint in the request body', () => {
      service.unsubscribe('https://fcm.example/push/abc').subscribe();
      const req = http.expectOne('/api/push/unsubscribe');
      expect(req.request.method).toBe('DELETE');
      expect(req.request.body).toEqual({ endpoint: 'https://fcm.example/push/abc' });
      req.flush(null);
    });

    it('uses the provided endpoint — not hardcoded — so any registration can be removed', () => {
      service.unsubscribe('https://different.example/xyz').subscribe();
      const req = http.expectOne('/api/push/unsubscribe');
      expect(req.request.body.endpoint).toBe('https://different.example/xyz');
      req.flush(null);
    });
  });

  // ── requestPermission() ───────────────────────────────────────────────────
  //
  // NOTE: requestPermission() relies on navigator.serviceWorker and PushManager,
  // which are not implemented in jsdom. Full happy-path tests (subscribe flow)
  // require a real browser or Playwright E2E. The tests below cover the guard
  // clause that protects non-supporting browsers.

  describe('requestPermission()', () => {
    it('returns null when serviceWorker is not supported in the browser', async () => {
      // jsdom does not implement navigator.serviceWorker, so this guard fires
      const result = await service.requestPermission('test-key');
      expect(result).toBeNull();
    });

    it('returns null when PushManager is not available', async () => {
      // Simulate a browser that has serviceWorker but no PushManager
      vi.stubGlobal('navigator', { ...navigator, serviceWorker: {} });
      // PushManager is not defined in jsdom, so the guard short-circuits
      const result = await service.requestPermission('test-key');
      expect(result).toBeNull();
      vi.unstubAllGlobals();
    });
  });
});
