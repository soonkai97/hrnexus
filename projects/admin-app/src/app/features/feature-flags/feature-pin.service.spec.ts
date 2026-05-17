import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { FeaturePinService } from './feature-pin.service';

const TOKEN_KEY   = 'nc_feature_token';
const EXPIRES_KEY = 'nc_feature_expires';

function futureIso(offsetMs = 3_600_000): string {
  return new Date(Date.now() + offsetMs).toISOString();
}

function pastIso(): string {
  return new Date(Date.now() - 1000).toISOString();
}

/**
 * FeaturePinService — sessionStorage-backed unlock token for the feature-flags admin UI.
 *
 * WHY these tests matter:
 *  - This is the only authentication layer protecting the feature-flags management screen.
 *    An expired token must be treated as locked, not unlocked.
 *  - minutesRemaining() is shown in the UI — returning a stale or negative value
 *    confuses admins about their remaining access window.
 *  - lock() and unlock() must atomically write/clear BOTH sessionStorage keys.
 */
describe('FeaturePinService', () => {
  let service: FeaturePinService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FeaturePinService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); sessionStorage.clear(); });

  // ── isUnlocked() ──────────────────────────────────────────────────────────

  it('returns false when no token exists', () => {
    expect(service.isUnlocked()).toBe(false);
  });

  it('returns false when token exists but expires in the past', () => {
    sessionStorage.setItem(TOKEN_KEY,   'tok');
    sessionStorage.setItem(EXPIRES_KEY, pastIso());
    expect(service.isUnlocked()).toBe(false);
  });

  it('returns true when token exists and expires in the future', () => {
    sessionStorage.setItem(TOKEN_KEY,   'tok');
    sessionStorage.setItem(EXPIRES_KEY, futureIso());
    expect(service.isUnlocked()).toBe(true);
  });

  it('returns false when expires key is missing even if token exists', () => {
    sessionStorage.setItem(TOKEN_KEY, 'tok');
    expect(service.isUnlocked()).toBe(false);
  });

  // ── getToken() ────────────────────────────────────────────────────────────

  it('returns the token string when unlocked', () => {
    sessionStorage.setItem(TOKEN_KEY,   'my-token');
    sessionStorage.setItem(EXPIRES_KEY, futureIso());
    expect(service.getToken()).toBe('my-token');
  });

  it('returns null when locked', () => {
    expect(service.getToken()).toBeNull();
  });

  // ── minutesRemaining() ────────────────────────────────────────────────────

  it('returns 0 when not unlocked', () => {
    expect(service.minutesRemaining()).toBe(0);
  });

  it('returns approximate remaining minutes when unlocked — within ±1 min', () => {
    sessionStorage.setItem(EXPIRES_KEY, new Date(Date.now() + 30 * 60000).toISOString());
    const mins = service.minutesRemaining();
    expect(mins).toBeGreaterThanOrEqual(29);
    expect(mins).toBeLessThanOrEqual(30);
  });

  it('returns 0 (not negative) for an already-expired token', () => {
    sessionStorage.setItem(EXPIRES_KEY, pastIso());
    expect(service.minutesRemaining()).toBe(0);
  });

  // ── unlock() ──────────────────────────────────────────────────────────────

  it('unlock() POST /api/feature-flags/unlock with the pin', async () => {
    const promise = firstValueFrom(service.unlock('1234'));
    const req = http.expectOne('/api/feature-flags/unlock');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ pin: '1234' });
    req.flush({ token: 'tok-xyz', expiresAt: futureIso() });
    await promise;
  });

  it('unlock() stores token and expiresAt in sessionStorage', async () => {
    const expiresAt = futureIso(60 * 60000);
    const promise = firstValueFrom(service.unlock('1234'));
    http.expectOne('/api/feature-flags/unlock').flush({ token: 'tok-xyz', expiresAt });
    await promise;
    expect(sessionStorage.getItem(TOKEN_KEY)).toBe('tok-xyz');
    expect(sessionStorage.getItem(EXPIRES_KEY)).toBe(expiresAt);
  });

  it('unlock() leaves the service in unlocked state after success', async () => {
    const promise = firstValueFrom(service.unlock('0000'));
    http.expectOne('/api/feature-flags/unlock').flush({ token: 'tok', expiresAt: futureIso() });
    await promise;
    expect(service.isUnlocked()).toBe(true);
  });

  // ── lock() ────────────────────────────────────────────────────────────────

  it('lock() clears both sessionStorage keys', () => {
    sessionStorage.setItem(TOKEN_KEY,   'tok');
    sessionStorage.setItem(EXPIRES_KEY, futureIso());
    service.lock();
    expect(sessionStorage.getItem(TOKEN_KEY)).toBeNull();
    expect(sessionStorage.getItem(EXPIRES_KEY)).toBeNull();
    expect(service.isUnlocked()).toBe(false);
  });
});
