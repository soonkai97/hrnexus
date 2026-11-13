import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideRouter, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { fakeUser } from '../../testing/test-helpers';
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
 *  - The access token now lives in an httpOnly cookie JS can't read, so the guard
 *    gates purely on the locally-known user session; the server enforces expiry.
 */
describe('authGuard', () => {
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr()), provideRouter([])],
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
      providers: [provideHttpClient(withXhr()), provideRouter([])],
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
