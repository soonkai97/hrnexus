import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AuthService } from './auth.service';
import { AuthUser } from '../models';

function fakeUser(roles: string[]): AuthUser {
  // `roles` are job NAMES, for display only.
  return {
    id: 'u1', staffId: 'NC001', fullName: 'Test User', email: 't@test.com', roles,
  };
}

describe('AuthService — session', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

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
