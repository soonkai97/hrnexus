import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { makeJwt, fakeUser } from '../../testing/test-helpers';

const ACCESS_TOKEN_KEY = 'access_token';
const REFRESH_TOKEN_KEY = 'refresh_token';
const USER_KEY = 'auth_user';

/**
 * AuthService — hasValidToken() and logout()
 *
 * WHY these tests matter:
 *  - hasValidToken() is the primary defence against expired-session API storms:
 *    guards block navigation instead of firing 401 requests to every endpoint.
 *  - logout() must atomically wipe ALL session keys — any leftover key could
 *    allow partial re-authentication or expose user data to the next browser session.
 */
describe('AuthService — hasValidToken()', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  it('returns false when no access token is stored', () => {
    expect(service.hasValidToken()).toBe(false);
  });

  it('returns true for a token that expires in the future', () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, makeJwt(3600)); // +1 hour
    expect(service.hasValidToken()).toBe(true);
  });

  it('returns false for a token that expired in the past — triggers login redirect', () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, makeJwt(-60)); // −1 minute
    expect(service.hasValidToken()).toBe(false);
  });

  it('returns false for a token expiring right at the current second (boundary)', () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, makeJwt(0)); // exp == now
    // (payload.exp > Date.now()/1000) is strict greater-than, so 0 offset is false
    expect(service.hasValidToken()).toBe(false);
  });

  it('returns false for a token string that is not a valid JWT', () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'not-a-jwt');
    expect(service.hasValidToken()).toBe(false);
  });

  it('returns false for a JWT with a malformed payload section', () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'header.!!!bad-base64!!!.sig');
    expect(service.hasValidToken()).toBe(false);
  });
});

describe('AuthService — logout()', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([{ path: 'auth/login', redirectTo: '' }])],
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  function seedSession(): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, makeJwt(3600));
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');
    localStorage.setItem(USER_KEY, JSON.stringify(fakeUser(['employee'])));
    (service as any)._user.set(fakeUser(['employee']));
  }

  it('clears access_token from localStorage', () => {
    seedSession();
    service.logout();
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
  });

  it('clears refresh_token from localStorage', () => {
    seedSession();
    service.logout();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('clears auth_user from localStorage', () => {
    seedSession();
    service.logout();
    expect(localStorage.getItem(USER_KEY)).toBeNull();
  });

  it('sets _user signal to null so isAuthenticated() returns false', () => {
    seedSession();
    expect(service.isAuthenticated()).toBe(true);
    service.logout();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('is idempotent — calling logout() twice does not throw', () => {
    seedSession();
    expect(() => { service.logout(); service.logout(); }).not.toThrow();
  });
});

describe('AuthService — evictStaleTokens()', () => {
  afterEach(() => localStorage.clear());

  it('removes mock-* tokens on construction so dev leftovers never reach production guards', () => {
    // Seed a mock token BEFORE constructing the service
    localStorage.setItem(ACCESS_TOKEN_KEY, 'mock-dev-token');
    localStorage.setItem(USER_KEY, JSON.stringify(fakeUser(['admin'])));

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    const svc = TestBed.inject(AuthService);

    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    expect(svc.isAuthenticated()).toBe(false);
  });

  it('leaves real JWT tokens untouched', () => {
    const jwt = makeJwt(3600);
    localStorage.setItem(ACCESS_TOKEN_KEY, jwt);
    localStorage.setItem(USER_KEY, JSON.stringify(fakeUser(['employee'])));

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    const svc = TestBed.inject(AuthService);

    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(jwt);
    expect(svc.isAuthenticated()).toBe(true);
  });
});
