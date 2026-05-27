import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { SwUpdate, VersionEvent } from '@angular/service-worker';
import { MessageService } from 'primeng/api';
import { AppUpdateService } from './app-update.service';
import { ToastService } from './toast.service';

/**
 * AppUpdateService — detects new service-worker versions and reloads the app.
 *
 * WHY these tests matter:
 *  - When SwUpdate is disabled (dev mode / unsupported browser) the service must
 *    do nothing — no errors, no subscriptions.
 *  - A VERSION_READY event must show an info toast so the user knows a refresh
 *    is coming, then call activateUpdate() before reloading.
 *  - Only VERSION_READY events should trigger a reload; other event types
 *    (VERSION_DETECTED, VERSION_INSTALLATION_FAILED) must be ignored.
 */
describe('AppUpdateService', () => {
  let service: AppUpdateService;
  let toast: ToastService;
  let versionUpdates$: Subject<VersionEvent>;

  const makeSwUpdate = (isEnabled: boolean) => {
    versionUpdates$ = new Subject<VersionEvent>();
    return {
      isEnabled,
      versionUpdates: versionUpdates$.asObservable(),
      activateUpdate: vi.fn().mockResolvedValue(undefined),
    };
  };

  function setup(isEnabled = true) {
    const swStub = makeSwUpdate(isEnabled);
    TestBed.configureTestingModule({
      providers: [
        MessageService,
        { provide: SwUpdate, useValue: swStub },
      ],
    });
    service = TestBed.inject(AppUpdateService);
    toast   = TestBed.inject(ToastService);
    vi.spyOn(toast, 'show');
    return swStub;
  }

  afterEach(() => vi.restoreAllMocks());

  // ── disabled (dev mode / unsupported browser) ─────────────────────────────

  it('init() does nothing when SwUpdate is disabled', () => {
    const sw = setup(false);
    service.init();
    // Emitting any event must not trigger toast or activateUpdate
    versionUpdates$.next({ type: 'VERSION_READY', currentVersion: {} as any, latestVersion: {} as any });
    expect(toast.show).not.toHaveBeenCalled();
    expect(sw.activateUpdate).not.toHaveBeenCalled();
  });

  // ── VERSION_READY handling ────────────────────────────────────────────────

  it('shows an info toast when a VERSION_READY event fires', () => {
    setup(true);
    service.init();
    versionUpdates$.next({ type: 'VERSION_READY', currentVersion: {} as any, latestVersion: {} as any });
    expect(toast.show).toHaveBeenCalledWith(
      expect.stringContaining('New version'),
      'info',
      expect.any(Number),
    );
  });

  it('calls activateUpdate() then reloads after the toast delay', async () => {
    vi.useFakeTimers();
    const sw = setup(true);
    // jsdom's window.location.reload is non-configurable — replace the whole location object
    const reloadFn = vi.fn();
    Object.defineProperty(window, 'location', { value: { ...window.location, reload: reloadFn }, writable: true, configurable: true });
    service.init();
    versionUpdates$.next({ type: 'VERSION_READY', currentVersion: {} as any, latestVersion: {} as any });
    // Before the timeout fires nothing should have happened yet
    expect(sw.activateUpdate).not.toHaveBeenCalled();
    // Advance past the 3-second delay
    await vi.runAllTimersAsync();
    expect(sw.activateUpdate).toHaveBeenCalledTimes(1);
    expect(reloadFn).toHaveBeenCalledTimes(1);
    vi.useRealTimers();
  });

  // ── other event types are ignored ─────────────────────────────────────────

  it('ignores VERSION_DETECTED events — does not toast or reload', () => {
    const sw = setup(true);
    service.init();
    versionUpdates$.next({ type: 'VERSION_DETECTED', version: {} as any });
    expect(toast.show).not.toHaveBeenCalled();
    expect(sw.activateUpdate).not.toHaveBeenCalled();
  });

  it('ignores VERSION_INSTALLATION_FAILED events — does not toast or reload', () => {
    const sw = setup(true);
    service.init();
    versionUpdates$.next({ type: 'VERSION_INSTALLATION_FAILED', version: {} as any, error: 'err' });
    expect(toast.show).not.toHaveBeenCalled();
    expect(sw.activateUpdate).not.toHaveBeenCalled();
  });
});
