import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { FeaturePinService } from './feature-pin.service';
import { featurePinGuard } from './feature-pin.guard';

const TOKEN_KEY   = 'nc_feature_token';
const EXPIRES_KEY = 'nc_feature_expires';

const route = {} as ActivatedRouteSnapshot;
const state = {} as RouterStateSnapshot;

/**
 * featurePinGuard — protects the feature-flags management screen.
 *
 * WHY these tests matter:
 *  - Feature-flags management changes which premium features are live for all staff.
 *    Unrestricted access would let any manager toggle billing-relevant features.
 *  - The guard is the sole enforcement layer — if it passes for an expired token,
 *    the screen is completely unprotected.
 */
describe('featurePinGuard', () => {
  let pinSvc: FeaturePinService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'features/unlock', redirectTo: '' }]),
      ],
    });
    pinSvc = TestBed.inject(FeaturePinService);
  });

  afterEach(() => sessionStorage.clear());

  it('returns true when the service reports unlocked', () => {
    sessionStorage.setItem(TOKEN_KEY,   'tok');
    sessionStorage.setItem(EXPIRES_KEY, new Date(Date.now() + 3_600_000).toISOString());

    const result = TestBed.runInInjectionContext(() => featurePinGuard(route, state));
    expect(result).toBe(true);
  });

  it('redirects to /features/unlock when the service reports locked', () => {
    // No token in sessionStorage
    const result = TestBed.runInInjectionContext(() => featurePinGuard(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/features/unlock');
  });

  it('redirects when token exists but is expired', () => {
    sessionStorage.setItem(TOKEN_KEY,   'tok');
    sessionStorage.setItem(EXPIRES_KEY, new Date(Date.now() - 1000).toISOString());

    const result = TestBed.runInInjectionContext(() => featurePinGuard(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/features/unlock');
  });
});
