import { TestBed } from '@angular/core/testing';
import { SignalRService } from './signalr.service';

/**
 * SignalRService — WebSocket hub for live stats and push notifications.
 *
 * WHY these tests matter:
 *  - hubState and isConnected signals are read by the UI to show connection
 *    indicators; wrong initial state would show a misleading "connected" badge.
 *  - stopConnection() must reset hubState to 'disconnected' and clear the hub
 *    reference so subsequent calls to startConnection() can create a fresh hub.
 *  - ngOnDestroy() must complete the Subject streams so downstream RxJS
 *    subscribers don't leak memory when the service is destroyed.
 *
 * NOTE: Full connection tests (startConnection → HubConnection.start()) require
 * a real SignalR server or a deep mock of @microsoft/signalr's HubConnectionBuilder.
 * The @microsoft/signalr library establishes WebSocket connections via XHR/Fetch
 * negotiate which jsdom cannot fulfil. Those paths are covered by E2E tests
 * (tests/e2e/) against the Docker deployment. Here we test only:
 *  - Initial signal state
 *  - State transitions controlled by the service itself (stopConnection, ngOnDestroy)
 *  - The computed isConnected derived signal
 *  - Observable surface (liveStats$, notifications$) — that they are observable
 *    and do not error on subscribe
 */
describe('SignalRService', () => {
  let service: SignalRService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SignalRService],
    });
    service = TestBed.inject(SignalRService);
  });

  afterEach(() => {
    // Ensure the service is cleaned up between tests
    service.ngOnDestroy();
  });

  // ── Initial state ──────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with hubState = "disconnected"', () => {
      expect(service.hubState()).toBe('disconnected');
    });

    it('starts with isConnected = false because no hub has been started', () => {
      expect(service.isConnected()).toBe(false);
    });

    it('exposes liveStats$ as an Observable without throwing', () => {
      expect(() => service.liveStats$.subscribe()).not.toThrow();
    });

    it('exposes notifications$ as an Observable without throwing', () => {
      expect(() => service.notifications$.subscribe()).not.toThrow();
    });
  });

  // ── isConnected computed signal ────────────────────────────────────────────

  describe('isConnected (computed)', () => {
    it('is false when hubState is "disconnected"', () => {
      // Default state
      expect(service.isConnected()).toBe(false);
    });

    it('is false when hubState is "connecting"', () => {
      // Access private hubState via the signal setter to simulate mid-connect
      (service as any).hubState.set('connecting');
      expect(service.isConnected()).toBe(false);
    });

    it('is true only when hubState is "connected"', () => {
      (service as any).hubState.set('connected');
      expect(service.isConnected()).toBe(true);
    });

    it('is false when hubState is "reconnecting"', () => {
      (service as any).hubState.set('reconnecting');
      expect(service.isConnected()).toBe(false);
    });

    it('is false when hubState is "failed"', () => {
      (service as any).hubState.set('failed');
      expect(service.isConnected()).toBe(false);
    });
  });

  // ── stopConnection() ──────────────────────────────────────────────────────

  describe('stopConnection()', () => {
    it('sets hubState to "disconnected"', () => {
      // Manually put the state into a non-disconnected value first
      (service as any).hubState.set('connected');
      service.stopConnection();
      expect(service.hubState()).toBe('disconnected');
    });

    it('clears the hub reference so startConnection() can create a fresh hub', () => {
      // If hub is null after stop, the guard `if (this.hub) return` in startConnection will not fire
      service.stopConnection();
      expect((service as any).hub).toBeNull();
    });

    it('is idempotent — calling it twice does not throw', () => {
      expect(() => {
        service.stopConnection();
        service.stopConnection();
      }).not.toThrow();
    });
  });

  // ── ngOnDestroy() ─────────────────────────────────────────────────────────

  describe('ngOnDestroy()', () => {
    it('completes liveStats$ so subscribers do not receive further events', () => {
      let completed = false;
      service.liveStats$.subscribe({ complete: () => (completed = true) });
      service.ngOnDestroy();
      expect(completed).toBe(true);
    });

    it('completes notifications$ so subscribers do not leak after destroy', () => {
      let completed = false;
      service.notifications$.subscribe({ complete: () => (completed = true) });
      service.ngOnDestroy();
      expect(completed).toBe(true);
    });

    it('resets hubState to "disconnected" on destroy', () => {
      (service as any).hubState.set('connected');
      service.ngOnDestroy();
      expect(service.hubState()).toBe('disconnected');
    });
  });

  // ── startConnection() guard ───────────────────────────────────────────────
  //
  // NOTE: We cannot call startConnection() fully because HubConnectionBuilder
  // attempts a negotiate HTTP call which jsdom cannot handle. We test only the
  // guard that prevents duplicate connections.

  describe('startConnection() — guard clause', () => {
    it('does not create a second hub if one already exists', () => {
      // Inject a fake hub so the guard fires
      const fakeHub = { stop: vi.fn(), on: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(), start: vi.fn().mockResolvedValue(undefined) };
      (service as any).hub = fakeHub;
      service.startConnection();
      // The hub reference should still be the same fake object (guard returned early)
      expect((service as any).hub).toBe(fakeHub);
    });
  });

  // ── connect() — maintenance-mode 503 handling ─────────────────────────────

  describe('connect() — maintenance 503 detection', () => {
    let navigatedHref: string;

    beforeEach(() => {
      navigatedHref = '';
      // jsdom does not allow assigning window.location.href directly; intercept
      // via Object.defineProperty so we can assert without actually navigating.
      Object.defineProperty(window, 'location', {
        value: { ...window.location, set href(v: string) { navigatedHref = v; } },
        writable: true,
        configurable: true,
      });
    });

    it('redirects to /maintenance.html when hub.start() rejects with a 503 status-code message', async () => {
      const fakeHub = {
        stop: vi.fn(), on: vi.fn(),
        onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(),
        start: vi.fn().mockRejectedValue(new Error("Error: Status code '503'")),
      };
      (service as any).hub = fakeHub;
      // Call connect() directly — it is private but accessible via cast
      await (service as any).connect();
      expect(navigatedHref).toBe('/maintenance.html');
    });

    it('redirects to /maintenance.html when hub.start() rejects with an HTML body', async () => {
      const fakeHub = {
        stop: vi.fn(), on: vi.fn(),
        onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(),
        start: vi.fn().mockRejectedValue(new Error('<!DOCTYPE html><html>maintenance</html>')),
      };
      (service as any).hub = fakeHub;
      await (service as any).connect();
      expect(navigatedHref).toBe('/maintenance.html');
    });

    it('sets hubState to "failed" for non-maintenance errors (e.g. network error)', async () => {
      const fakeHub = {
        stop: vi.fn(), on: vi.fn(),
        onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(),
        start: vi.fn().mockRejectedValue(new Error('Network error')),
      };
      (service as any).hub = fakeHub;
      await (service as any).connect();
      expect(service.hubState()).toBe('failed');
      expect(navigatedHref).toBe('');
    });
  });
});
