import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AuthService } from './auth.service';
import { AuthUser } from '../models';

function fakeUser(roles: string[]): AuthUser {
  // `roles` are NAMES (drives the auth tier); `jobs` mirrors them with ids for the shift dropdown.
  return {
    id: 'u1', staffId: 'NC001', fullName: 'Test User', email: 't@test.com', roles,
    jobs: roles.map((name, i) => ({ id: `job-${i}`, name })),
  };
}

describe('AuthService — userRole and hasRole', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  // ── userRole ──────────────────────────────────────────────────────────────

  it('is null when no user is logged in', () => {
    expect(service.userRole()).toBeNull();
  });

  it('resolves to employee for any non-admin/non-manager role', () => {
    // A bartender, server, etc. has job-specific roles that aren't admin/manager
    (service as any)._user.set(fakeUser(['BARTENDER']));
    expect(service.userRole()).toBe('employee');
  });

  it('resolves to manager when roles contains manager', () => {
    (service as any)._user.set(fakeUser(['MANAGER']));
    expect(service.userRole()).toBe('manager');
  });

  it('resolves to admin when roles contains admin', () => {
    (service as any)._user.set(fakeUser(['SUPERADMIN']));
    expect(service.userRole()).toBe('admin');
  });

  it('admin takes precedence over manager when staff holds both roles', () => {
    // Highest privilege wins — access decisions must not accidentally downgrade
    (service as any)._user.set(fakeUser(['MANAGER', 'SUPERADMIN']));
    expect(service.userRole()).toBe('admin');
  });

  it('resolves to null for an empty roles array', () => {
    (service as any)._user.set(fakeUser([]));
    expect(service.userRole()).toBeNull();
  });

  // ── hasRole ───────────────────────────────────────────────────────────────

  it('returns false for all roles when not logged in', () => {
    expect(service.hasRole('admin')).toBe(false);
    expect(service.hasRole('manager')).toBe(false);
    expect(service.hasRole('employee')).toBe(false);
  });

  it('returns true only for the resolved permission level', () => {
    (service as any)._user.set(fakeUser(['MANAGER']));
    expect(service.hasRole('manager')).toBe(true);
    expect(service.hasRole('admin')).toBe(false);
    expect(service.hasRole('employee')).toBe(false);
  });

  it('returns true when one of multiple supplied roles matches', () => {
    // Admin and manager UIs both call hasRole('admin', 'manager') to gate shared views
    (service as any)._user.set(fakeUser(['MANAGER']));
    expect(service.hasRole('admin', 'manager')).toBe(true);
  });

  it('returns false when none of multiple supplied roles match', () => {
    (service as any)._user.set(fakeUser(['BARTENDER']));
    expect(service.hasRole('admin', 'manager')).toBe(false);
  });

  // ── isAuthenticated ───────────────────────────────────────────────────────

  it('is false when user signal is null', () => {
    expect(service.isAuthenticated()).toBe(false);
  });

  it('is true once a user is loaded into the signal', () => {
    (service as any)._user.set(fakeUser(['BARTENDER']));
    expect(service.isAuthenticated()).toBe(true);
  });

  // ── logout / loggingOut ─────────────────────────────────────────────────────

  it('clears the user and flags loggingOut so guards skip the unsaved-changes prompt', async () => {
    (service as any)._user.set(fakeUser(['BARTENDER']));

    service.logout();

    // Set synchronously, before the route guard runs — otherwise the unsaved-changes
    // dialog would deadlock behind the "Signing out…" overlay.
    expect(service.loggingOut()).toBe(true);
    expect(service.user()).toBeNull();

    // Resets once the navigation settles so a later session's guard works normally again.
    await new Promise(r => setTimeout(r));
    expect(service.loggingOut()).toBe(false);
  });
});

// ── refreshToken() — only a real 401 kills the session ─────────────────────────
// WHY: a transient refresh failure (dropped mobile request, 5xx, or a 503 during a deploy) must NOT
// log the user out — the refresh token is still valid. Only a genuine 401 (server rejected the
// token) means the session is dead. This is the fix for staff getting logged out on flaky wifi.
describe('AuthService — refreshToken() logout policy', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('logs out on a genuine 401 (refresh token rejected)', () => {
    const logoutSpy = vi.spyOn(service, 'logout').mockImplementation(() => {});
    service.refreshToken().subscribe({ error: () => {} });
    http.expectOne('/api/auth/refresh').flush({ error: 'invalid' }, { status: 401, statusText: 'Unauthorized' });
    expect(logoutSpy).toHaveBeenCalled();
  });

  it('does NOT log out on a 5xx (transient server error / deploy)', () => {
    const logoutSpy = vi.spyOn(service, 'logout').mockImplementation(() => {});
    service.refreshToken().subscribe({ error: () => {} });
    http.expectOne('/api/auth/refresh').flush({ error: 'boom' }, { status: 500, statusText: 'Server Error' });
    expect(logoutSpy).not.toHaveBeenCalled();
  });

  it('does NOT log out on a network error (status 0)', () => {
    const logoutSpy = vi.spyOn(service, 'logout').mockImplementation(() => {});
    service.refreshToken().subscribe({ error: () => {} });
    http.expectOne('/api/auth/refresh').error(new ProgressEvent('error'));
    expect(logoutSpy).not.toHaveBeenCalled();
  });
});
