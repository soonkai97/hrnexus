import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { AuthResponse } from '../models';
import { fakeUser } from '../../testing/test-helpers';

const ACCESS_TOKEN_KEY  = 'access_token';
const REFRESH_TOKEN_KEY = 'refresh_token';
const USER_KEY          = 'auth_user';

function makeAuthResponse(roles: string[] = ['employee']): AuthResponse {
  // Tokens are no longer in the body — they're set as httpOnly cookies by the server.
  return { expiresIn: 3600, user: fakeUser(roles) };
}

/**
 * AuthService — HTTP-backed methods (login, refresh, logout, password flows)
 *
 * WHY these tests matter (post httpOnly-cookie migration):
 *  - login() must persist ONLY the non-sensitive user profile; tokens live in httpOnly cookies
 *    the browser manages, so they must NOT be written to localStorage anymore (XSS-readable).
 *  - refreshToken() is called by the JWT interceptor on 401s; it sends an empty body (the refresh
 *    token rides in its cookie) and must call logout() on failure so the user isn't stuck.
 *  - logout() must hit the server so the cookies are expired + the DB refresh token cleared.
 */
describe('AuthService — login()', () => {
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

  it('POST /api/auth/login with the supplied credentials', async () => {
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'pass' }));
    const req = http.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ staffId: 'NC001', password: 'pass' });
    req.flush(makeAuthResponse());
    await promise;
  });

  it('does NOT write tokens to localStorage after login (they are httpOnly cookies)', async () => {
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(makeAuthResponse());
    await promise;
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('stores auth_user JSON in localStorage after successful login', async () => {
    const res = makeAuthResponse(['admin']);
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(res);
    await promise;
    expect(JSON.parse(localStorage.getItem(USER_KEY)!)).toEqual(res.user);
  });

  it('sets _user signal so isAuthenticated() returns true', async () => {
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(makeAuthResponse());
    await promise;
    expect(service.isAuthenticated()).toBe(true);
  });

  it('purges any legacy localStorage tokens on construction', () => {
    // (service already constructed in beforeEach with empty storage; assert keys stay clear)
    localStorage.setItem(ACCESS_TOKEN_KEY, 'old-jwt');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'old-rt');
    // Re-create the service to trigger the constructor cleanup.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(AuthService);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });
});

describe('AuthService — refreshToken()', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      // Include auth/login route so logout() navigation doesn't throw NG04002
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'auth/login', redirectTo: '' }])],
    });
    service = TestBed.inject(AuthService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('POST /api/auth/refresh with an empty body (refresh token rides in the cookie)', async () => {
    const promise = firstValueFrom(service.refreshToken());
    const req = http.expectOne('/api/auth/refresh');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush(makeAuthResponse());
    await promise;
  });

  it('stores the refreshed user profile on success', async () => {
    const res = makeAuthResponse(['manager']);
    const promise = firstValueFrom(service.refreshToken());
    http.expectOne('/api/auth/refresh').flush(res);
    await promise;
    expect(JSON.parse(localStorage.getItem(USER_KEY)!)).toEqual(res.user);
  });

  it('calls logout() and re-throws on API 401 — prevents infinite retry loop', async () => {
    (service as any)._user.set(fakeUser(['employee']));

    const promise = firstValueFrom(service.refreshToken());
    http.expectOne('/api/auth/refresh').flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    // logout() fires a best-effort server call to expire the cookies.
    http.expectOne('/api/auth/logout').flush(null);

    await expect(promise).rejects.toBeTruthy();
    expect(service.isAuthenticated()).toBe(false); // logout() was called
  });
});

describe('AuthService — logout()', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'auth/login', redirectTo: '' }])],
    });
    service = TestBed.inject(AuthService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('POSTs /api/auth/logout and clears the local user + auth_user key', () => {
    localStorage.setItem(USER_KEY, JSON.stringify(fakeUser(['employee'])));
    (service as any)._user.set(fakeUser(['employee']));

    service.logout();

    http.expectOne('/api/auth/logout').flush(null);
    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(USER_KEY)).toBeNull();
  });

  it('still clears local state even if the server logout call errors', () => {
    (service as any)._user.set(fakeUser(['employee']));

    service.logout();

    http.expectOne('/api/auth/logout').flush(null, { status: 500, statusText: 'Server Error' });
    expect(service.isAuthenticated()).toBe(false);
  });
});

describe('AuthService — password flows', () => {
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

  it('forgotPassword() POST /api/auth/forgot-password and returns result', async () => {
    const promise = firstValueFrom(service.forgotPassword('NC001'));
    const req = http.expectOne('/api/auth/forgot-password');
    expect(req.request.body).toEqual({ staffId: 'NC001' });
    req.flush({ found: true });
    const result = await promise;
    expect(result.found).toBe(true);
  });

  it('forgotPassword() returns found=false when staff ID does not exist', async () => {
    const promise = firstValueFrom(service.forgotPassword('UNKNOWN'));
    http.expectOne('/api/auth/forgot-password').flush({ found: false });
    const result = await promise;
    expect(result.found).toBe(false);
  });
});
