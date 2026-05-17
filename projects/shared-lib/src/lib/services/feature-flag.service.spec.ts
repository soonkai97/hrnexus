import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { FeatureFlagService, FeatureKey, FlagMap } from './feature-flag.service';

const ALL_KEYS: FeatureKey[] = [
  'otRules', 'payroll', 'tips', 'tipsHistory', 'paySummary',
  'messagingAdmin', 'announcements', 'messagingStaff',
];

function makeAllFlags(value: boolean): Partial<FlagMap> {
  return Object.fromEntries(ALL_KEYS.map(k => [k, value])) as Partial<FlagMap>;
}

/**
 * FeatureFlagService — load, toggle, enableAll, disableAll
 *
 * WHY these tests matter:
 *  - load() sets the `loaded` signal that guards depend on; failure must still mark loaded=true
 *    so guards don't wait forever.
 *  - toggle() is optimistic — the UI must revert cleanly on API error or admins see stale state.
 *  - enableAll()/disableAll() are bulk admin operations; a revert on error protects data integrity.
 *  - isEnabled() is called on every navigation — it must read live signal state, not cache.
 */
describe('FeatureFlagService', () => {
  let service: FeatureFlagService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FeatureFlagService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── load() ────────────────────────────────────────────────────────────────

  it('load() sets loaded=true on success', async () => {
    const promise = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush({ payroll: true });
    await promise;
    expect(service.loaded()).toBe(true);
  });

  it('load() merges returned flags with DEFAULTS — keys missing from API stay false', async () => {
    const promise = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush({ payroll: true });
    await promise;
    expect(service.isEnabled('payroll')).toBe(true);
    expect(service.isEnabled('tips')).toBe(false); // default
  });

  it('load() sets loaded=true even on API error — guards must not block indefinitely', async () => {
    const promise = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').error(new ErrorEvent('Network error'));
    await promise;
    expect(service.loaded()).toBe(true);
  });

  // ── isEnabled() ───────────────────────────────────────────────────────────

  it('isEnabled() returns false for all flags by default before any load', () => {
    for (const key of ALL_KEYS) {
      expect(service.isEnabled(key)).toBe(false);
    }
  });

  // ── toggle() ──────────────────────────────────────────────────────────────

  it('toggle() immediately flips the flag (optimistic update)', async () => {
    // Load flags first
    const load = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush({ payroll: false });
    await load;

    const toggle = firstValueFrom(service.toggle('payroll'));
    // Before the API responds, the flag should already be true (optimistic)
    expect(service.isEnabled('payroll')).toBe(true);
    http.expectOne(r => r.method === 'PUT').flush(null);
    await toggle;
    expect(service.isEnabled('payroll')).toBe(true);
  });

  it('toggle() reverts the flag on API error — prevents stale optimistic state', async () => {
    const load = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush({ tips: true });
    await load;

    const toggle = firstValueFrom(service.toggle('tips').pipe());
    expect(service.isEnabled('tips')).toBe(false); // optimistic flip
    http.expectOne(r => r.method === 'PUT').error(new ErrorEvent('Server error'));
    await expect(toggle).rejects.toBeTruthy();
    expect(service.isEnabled('tips')).toBe(true); // reverted
  });

  it('toggle() sends PUT /api/feature-flags with the full updated flag map', async () => {
    const load = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush({});
    await load;

    firstValueFrom(service.toggle('announcements'));
    const req = http.expectOne(r => r.method === 'PUT');
    expect(req.request.url).toBe('/api/feature-flags');
    expect(req.request.body['announcements']).toBe(true);
    req.flush(null);
  });

  // ── enableAll() ───────────────────────────────────────────────────────────

  it('enableAll() sets all flags to true optimistically', async () => {
    const toggle = firstValueFrom(service.enableAll());
    for (const key of ALL_KEYS) {
      expect(service.isEnabled(key)).toBe(true);
    }
    http.expectOne(r => r.method === 'PUT').flush(null);
    await toggle;
  });

  it('enableAll() reverts all flags on API error', async () => {
    // Seed: only payroll enabled
    const load = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush({ payroll: true });
    await load;

    const toggle = firstValueFrom(service.enableAll());
    http.expectOne(r => r.method === 'PUT').error(new ErrorEvent('Server error'));
    await expect(toggle).rejects.toBeTruthy();
    expect(service.isEnabled('payroll')).toBe(true);  // previous state
    expect(service.isEnabled('tips')).toBe(false);    // previous state
  });

  // ── disableAll() ──────────────────────────────────────────────────────────

  it('disableAll() sets all flags to false optimistically', async () => {
    // Seed all enabled
    const load = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush(makeAllFlags(true));
    await load;

    const toggle = firstValueFrom(service.disableAll());
    for (const key of ALL_KEYS) {
      expect(service.isEnabled(key)).toBe(false);
    }
    http.expectOne(r => r.method === 'PUT').flush(null);
    await toggle;
  });

  it('disableAll() reverts to previous flags on API error', async () => {
    const load = firstValueFrom(service.load());
    http.expectOne('/api/feature-flags').flush(makeAllFlags(true));
    await load;

    const toggle = firstValueFrom(service.disableAll());
    http.expectOne(r => r.method === 'PUT').error(new ErrorEvent('Server error'));
    await expect(toggle).rejects.toBeTruthy();
    expect(service.isEnabled('payroll')).toBe(true); // reverted
  });
});
