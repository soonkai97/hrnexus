import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { FeatureFlagService, FeatureKey } from './feature-flag.service';

const ALL_KEYS: FeatureKey[] = ['payroll'];

/**
 * FeatureFlagService — load, isEnabled
 *
 * WHY these tests matter:
 *  - load() sets the `loaded` signal that guards depend on; failure must still mark loaded=true
 *    so guards don't wait forever.
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
    http.expectOne('/api/feature-flags').flush({}); // API returns no flags
    await promise;
    expect(service.isEnabled('payroll')).toBe(false); // default preserved
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
});
