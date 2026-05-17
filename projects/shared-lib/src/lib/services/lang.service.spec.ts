import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LangService } from './lang.service';

const STORAGE_KEY = 'nc_lang';

/**
 * LangService — manages UI language across signal, localStorage, and backend sync.
 *
 * WHY these tests matter:
 *  - A wrong language on first load breaks every piece of UI text for that user.
 *  - Preference must survive browser refreshes (localStorage) and re-logins (API sync).
 *  - syncToApi=false is used during API-driven sync to avoid an infinite loop of PATCH calls.
 *  - toggle() is used in the profile/account UI; it must alternate cleanly each call.
 */
describe('LangService', () => {
  let service: LangService;
  let http: HttpTestingController;

  function setup(storedLang?: string) {
    localStorage.clear();
    if (storedLang) localStorage.setItem(STORAGE_KEY, storedLang);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ defaultLanguage: 'en' }),
      ],
    });
    service = TestBed.inject(LangService);
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  // ── Initial state ──────────────────────────────────────────────────────────

  it('defaults to "en" when nothing is stored — new users always start in English', () => {
    setup();
    expect(service.current()).toBe('en');
  });

  it('restores stored "zh" so returning users keep their preferred language', () => {
    setup('zh');
    expect(service.current()).toBe('zh');
  });

  it('falls back to "en" when an unsupported value is in localStorage', () => {
    setup('fr'); // 'fr' is not in SUPPORTED
    expect(service.current()).toBe('en');
  });

  // ── set() ─────────────────────────────────────────────────────────────────

  it('set("zh") updates the signal immediately', () => {
    setup();
    service.set('zh');
    http.expectOne('/api/auth/language'); // consume the fire-and-forget
    expect(service.current()).toBe('zh');
  });

  it('set() persists the language to localStorage so refresh restores it', () => {
    setup();
    service.set('zh');
    http.expectOne('/api/auth/language');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('zh');
  });

  it('set() fires a PATCH to the backend with the new language', () => {
    setup();
    service.set('zh');
    const req = http.expectOne('/api/auth/language');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ language: 'zh' });
    req.flush({});
  });

  it('set() with syncToApi=false does NOT fire any HTTP request — prevents loop in syncFromApi', () => {
    setup();
    service.set('zh', false);
    http.expectNone('/api/auth/language');
    expect(service.current()).toBe('zh');
  });

  // ── toggle() ───────────────────────────────────────────────────────────────

  it('toggle() switches en → zh', () => {
    setup();
    service.toggle();
    http.expectOne('/api/auth/language');
    expect(service.current()).toBe('zh');
  });

  it('toggle() switches zh → en on second call', () => {
    setup('zh');
    service.toggle();
    http.expectOne('/api/auth/language');
    expect(service.current()).toBe('en');
  });

  it('toggle() alternates cleanly across three calls', () => {
    setup();
    service.toggle(); http.expectOne('/api/auth/language').flush({});
    service.toggle(); http.expectOne('/api/auth/language').flush({});
    service.toggle(); http.expectOne('/api/auth/language').flush({});
    expect(service.current()).toBe('zh');
  });

  // ── syncFromApi() ──────────────────────────────────────────────────────────

  it('syncFromApi() applies a valid language returned by the backend', () => {
    setup();
    service.syncFromApi();
    const req = http.expectOne('/api/auth/language');
    expect(req.request.method).toBe('GET');
    req.flush({ language: 'zh' });
    // syncFromApi calls set(lang, false) → no follow-up PATCH
    expect(service.current()).toBe('zh');
    http.expectNone('/api/auth/language');
  });

  it('syncFromApi() ignores unsupported languages returned by the backend', () => {
    setup();
    service.syncFromApi();
    http.expectOne('/api/auth/language').flush({ language: 'ja' });
    expect(service.current()).toBe('en'); // unchanged
  });

  it('syncFromApi() silently handles API errors without crashing the app', () => {
    setup();
    service.syncFromApi();
    http.expectOne('/api/auth/language').error(new ErrorEvent('Network error'));
    expect(service.current()).toBe('en'); // unchanged, no throw
  });
});
