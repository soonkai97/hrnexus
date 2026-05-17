import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { firstValueFrom, Observable } from 'rxjs';
import { FeatureFlagService } from '../services/feature-flag.service';
import { featureFlagGuard } from './feature-flag.guard';

// Stub route/state — guard doesn't use them
const route = {} as ActivatedRouteSnapshot;
const state = {} as RouterStateSnapshot;

/**
 * featureFlagGuard — gates feature routes so disabled features are inaccessible.
 *
 * WHY these tests matter:
 *  - Premium features (payroll, tips, messaging) should silently redirect when disabled,
 *    not crash or show blank pages.
 *  - On a fresh page load the Shell hasn't run yet, so the guard must trigger
 *    its own load() call — otherwise the feature is always treated as disabled.
 *  - The fallback route is configurable per-feature, so the user lands somewhere useful.
 */
describe('featureFlagGuard', () => {
  let featureSvc: FeatureFlagService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    featureSvc = TestBed.inject(FeatureFlagService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── Flags already loaded (in-app navigation) ──────────────────────────────

  describe('when flags are already loaded (in-app navigation)', () => {
    /** Load flags so loaded() === true before each guard test. */
    async function loadFlags(flags: Record<string, boolean> = {}): Promise<void> {
      const promise = firstValueFrom(featureSvc.load());
      http.expectOne('/api/feature-flags').flush(flags);
      await promise;
    }

    it('returns true when the flag is enabled', async () => {
      await loadFlags({ payroll: true });

      const result = TestBed.runInInjectionContext(() =>
        featureFlagGuard('payroll', '/dashboard')(route, state));

      expect(result).toBe(true);
    });

    it('redirects to the custom fallback when the flag is disabled', async () => {
      await loadFlags(); // payroll defaults to false

      const result = TestBed.runInInjectionContext(() =>
        featureFlagGuard('payroll', '/dashboard')(route, state));

      expect(result).toBeInstanceOf(UrlTree);
      expect((result as UrlTree).toString()).toBe('/dashboard');
    });

    it('redirects to "/" when no custom fallback is given', async () => {
      await loadFlags();

      const result = TestBed.runInInjectionContext(() =>
        featureFlagGuard('tips')(route, state));

      expect(result).toBeInstanceOf(UrlTree);
      expect((result as UrlTree).toString()).toBe('/');
    });

    it('does not fire any HTTP request when flags are already loaded', async () => {
      await loadFlags({ tips: true });
      TestBed.runInInjectionContext(() => featureFlagGuard('tips')(route, state));
      http.expectNone('/api/feature-flags');
    });
  });

  // ── Fresh page load (flags not yet loaded) ────────────────────────────────

  describe('when flags are NOT loaded (fresh page load)', () => {
    it('returns an Observable and resolves true after load() returns an enabled flag', async () => {
      const obs$ = TestBed.runInInjectionContext(() =>
        featureFlagGuard('announcements', '/home')(route, state)) as Observable<boolean | UrlTree>;

      // Flush the HTTP call BEFORE awaiting the Observable
      const promise = firstValueFrom(obs$);
      http.expectOne('/api/feature-flags').flush({ announcements: true });

      const result = await promise;
      expect(result).toBe(true);
    });

    it('returns an Observable and redirects after load() returns a disabled flag', async () => {
      const obs$ = TestBed.runInInjectionContext(() =>
        featureFlagGuard('messagingAdmin', '/home')(route, state)) as Observable<boolean | UrlTree>;

      const promise = firstValueFrom(obs$);
      http.expectOne('/api/feature-flags').flush({}); // messagingAdmin not in response → false

      const result = await promise;
      expect(result).toBeInstanceOf(UrlTree);
    });

    it('redirects gracefully when the API call errors — fails safe, not open', async () => {
      const obs$ = TestBed.runInInjectionContext(() =>
        featureFlagGuard('payroll', '/dashboard')(route, state)) as Observable<boolean | UrlTree>;

      const promise = firstValueFrom(obs$);
      http.expectOne('/api/feature-flags').error(new ErrorEvent('Network error'));

      const result = await promise;
      // Flag defaults to false on error → should redirect
      expect(result).toBeInstanceOf(UrlTree);
      expect((result as UrlTree).toString()).toBe('/dashboard');
    });
  });
});
