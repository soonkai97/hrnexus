import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { makeJwt, fakeUser } from '../../testing/test-helpers';
import { authGuard, roleGuard } from './auth.guard';

// Stub ActivatedRouteSnapshot / RouterStateSnapshot — guards don't use them
const route = {} as ActivatedRouteSnapshot;
const state = {} as RouterStateSnapshot;

/**
 * authGuard — protects every authenticated route.
 *
 * WHY these tests matter:
 *  - Without this guard, unauthenticated requests reach the API and produce
 *    noisy 401 storms that confuse users and waste bandwidth.
 *  - Expired-token check prevents a window where the JS session looks valid
 *    but every API call fails before the interceptor can redirect.
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

  it('returns true when user is authenticated and token is valid', () => {
    (auth as any)._user.set(fakeUser(['BARTENDER']));
    localStorage.setItem('access_token', makeJwt(3600)); // 1 hour ahead

    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBe(true);
  });

  it('redirects to /auth/login when no user is set', () => {
    // No user, no token
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/auth/login');
  });

  it('redirects to /auth/login when user exists but token is expired — prevents 401 storms', () => {
    (auth as any)._user.set(fakeUser(['BARTENDER']));
    localStorage.setItem('access_token', makeJwt(-60)); // 1 minute ago

    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/auth/login');
  });

  it('redirects to /auth/login when user exists but no token is stored', () => {
    (auth as any)._user.set(fakeUser(['BARTENDER']));
    // no access_token in localStorage

    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/auth/login');
  });
});

/**
 * roleGuard — restricts routes to specific permission levels.
 *
 * WHY these tests matter:
 *  - Admin-only routes (staff management, feature-flags) must never be reached by
 *    employees or managers — this guard is the only enforcement boundary in the SPA.
 *  - Multi-role support lets manager+admin share routes without duplicating guard config.
 */
describe('roleGuard', () => {
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  it('returns true when the user has the required role', () => {
    (auth as any)._user.set(fakeUser(['MANAGER']));
    const result = TestBed.runInInjectionContext(() => roleGuard('manager')(route, state));
    expect(result).toBe(true);
  });

  it('returns true for admin when admin role is required', () => {
    (auth as any)._user.set(fakeUser(['SUPERADMIN']));
    const result = TestBed.runInInjectionContext(() => roleGuard('admin')(route, state));
    expect(result).toBe(true);
  });

  it('redirects to /unauthorized when user lacks the required role', () => {
    (auth as any)._user.set(fakeUser(['BARTENDER'])); // resolves to 'employee'
    const result = TestBed.runInInjectionContext(() => roleGuard('admin')(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/unauthorized');
  });

  it('redirects to /unauthorized when manager tries to access admin-only route', () => {
    (auth as any)._user.set(fakeUser(['MANAGER']));
    const result = TestBed.runInInjectionContext(() => roleGuard('admin')(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/unauthorized');
  });

  it('returns true when user matches any of multiple allowed roles', () => {
    // Many admin routes use roleGuard("admin", "manager") — both must pass
    (auth as any)._user.set(fakeUser(['MANAGER']));
    const result = TestBed.runInInjectionContext(() => roleGuard('admin', 'manager')(route, state));
    expect(result).toBe(true);
  });

  it('redirects to /unauthorized when no user is logged in', () => {
    // _user is null → userRole() is null → hasRole() is false
    const result = TestBed.runInInjectionContext(() => roleGuard('admin', 'manager')(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/unauthorized');
  });

  it('employee cannot access manager-only route — least-privilege boundary', () => {
    (auth as any)._user.set(fakeUser(['CASHIER'])); // resolves to 'employee'
    const result = TestBed.runInInjectionContext(() => roleGuard('manager')(route, state));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/unauthorized');
  });
});
