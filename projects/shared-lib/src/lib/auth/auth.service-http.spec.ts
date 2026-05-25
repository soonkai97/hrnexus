import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { AuthResponse } from '../models';
import { makeJwt, fakeUser } from '../../testing/test-helpers';

const ACCESS_TOKEN_KEY  = 'access_token';
const REFRESH_TOKEN_KEY = 'refresh_token';
const USER_KEY          = 'auth_user';

function makeAuthResponse(roles: string[] = ['employee']): AuthResponse {
  return {
    accessToken:  makeJwt(3600),
    refreshToken: 'rt-abc',
    expiresIn:    3600,
    user: fakeUser(roles),
  };
}

/**
 * AuthService — HTTP-backed methods (login, refresh, password flows)
 *
 * WHY these tests matter:
 *  - login() must persist all three localStorage keys atomically — a partial
 *    write leaves the user in a broken half-authenticated state.
 *  - refreshToken() is called by the JWT interceptor on 401s; if it silently
 *    fails without calling logout(), the user sees a blank page.
 *  - forgotPassword / forceChangePassword / changePassword are security-critical
 *    paths that must forward payloads to the API unmodified.
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

  it('stores access_token in localStorage after successful login', async () => {
    const res = makeAuthResponse();
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(res);
    await promise;
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(res.accessToken);
  });

  it('stores refresh_token in localStorage after successful login', async () => {
    const res = makeAuthResponse();
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(res);
    await promise;
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe(res.refreshToken);
  });

  it('stores auth_user JSON in localStorage after successful login', async () => {
    const res = makeAuthResponse(['admin']);
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(res);
    await promise;
    expect(JSON.parse(localStorage.getItem(USER_KEY)!)).toEqual(res.user);
  });

  it('sets _user signal so isAuthenticated() returns true', async () => {
    const res = makeAuthResponse();
    const promise = firstValueFrom(service.login({ staffId: 'NC001', password: 'p' }));
    http.expectOne('/api/auth/login').flush(res);
    await promise;
    expect(service.isAuthenticated()).toBe(true);
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

  it('POST /api/auth/refresh with the stored refresh token', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-rt');
    const promise = firstValueFrom(service.refreshToken());
    const req = http.expectOne('/api/auth/refresh');
    expect(req.request.body).toEqual({ refreshToken: 'stored-rt' });
    req.flush(makeAuthResponse());
    await promise;
  });

  it('throws immediately when no refresh token is stored', async () => {
    await expect(firstValueFrom(service.refreshToken())).rejects.toThrow('No refresh token');
    http.expectNone('/api/auth/refresh');
  });

  it('calls logout() and re-throws on API 401 — prevents infinite retry loop', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'expired-rt');
    (service as any)._user.set(fakeUser(['employee']));

    const promise = firstValueFrom(service.refreshToken());
    http.expectOne('/api/auth/refresh').flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    await expect(promise).rejects.toBeTruthy();
    expect(service.isAuthenticated()).toBe(false); // logout() was called
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

  it('changePassword() POST /api/auth/change-password with old and new passwords', async () => {
    const req$ = service.changePassword({ currentPassword: 'old', newPassword: 'new' });
    const promise = firstValueFrom(req$);
    const req = http.expectOne('/api/auth/change-password');
    expect(req.request.method).toBe('POST');
    req.flush(null);
    await promise;
  });

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
