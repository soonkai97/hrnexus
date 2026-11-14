import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { fakeUser } from '../../testing/test-helpers';
import { authGuard } from './auth.guard';

// Stub ActivatedRouteSnapshot / RouterStateSnapshot — guards don't use them
const route = {} as ActivatedRouteSnapshot;
const state = {} as RouterStateSnapshot;

/**
 * authGuard — protects every authenticated route.
 *
 * WHY these tests matter:
 *  - Without this guard, unauthenticated requests reach the API and produce
 *    noisy 401 storms that confuse users and waste bandwidth.
 *  - The access token now lives in an httpOnly cookie JS can't read, so the guard
 *    gates purely on the locally-known user session; the server enforces expiry.
 */
describe('authGuard', () => {
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  it('returns true when a user session is known', () => {
    (auth as any)._user.set(fakeUser(['BARTENDER']));

    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBe(true);
  });

  it('redirects to /auth/login when no user is set', () => {
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/auth/login');
  });
});
