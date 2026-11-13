import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { apiErrorMessage, errorInterceptor } from './error.interceptor';

describe('apiErrorMessage', () => {
  it('extracts the curated message from { error: { error } }', () => {
    expect(apiErrorMessage({ error: { error: 'Insufficient balance.' } })).toBe('Insufficient balance.');
  });

  it('falls back to { error: { message } } when error is absent', () => {
    expect(apiErrorMessage({ error: { message: 'Bad thing.' } })).toBe('Bad thing.');
  });

  it('returns a raw string error body as-is', () => {
    expect(apiErrorMessage({ error: 'Plain string error' })).toBe('Plain string error');
  });

  it('returns undefined when the server supplied no message (caller localizes a fallback)', () => {
    expect(apiErrorMessage({ error: {} })).toBeUndefined();
    expect(apiErrorMessage({ error: '' })).toBeUndefined();
    expect(apiErrorMessage({})).toBeUndefined();
    expect(apiErrorMessage(null)).toBeUndefined();
    expect(apiErrorMessage(undefined)).toBeUndefined();
  });
});

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor]), withXhr()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('rethrows the original error (does not swallow it) and logs non-401/503 failures', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    let captured: { status?: number } | undefined;

    http.get('/api/thing').subscribe({ next: () => {}, error: (e) => (captured = e) });
    httpMock.expectOne('/api/thing').flush({ error: 'Boom' }, { status: 500, statusText: 'Server Error' });

    expect(captured?.status).toBe(500);   // error still reaches the caller
    expect(spy).toHaveBeenCalledOnce();    // and was logged centrally
  });

  it('does NOT log 401 (token-refresh noise handled by the jwt interceptor)', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});

    http.get('/api/secure').subscribe({ next: () => {}, error: () => {} });
    httpMock.expectOne('/api/secure').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(spy).not.toHaveBeenCalled();
  });
});
