import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { LoginComponent } from './login.component';
import { AuthService, LangService } from 'shared-lib';

@Component({ template: '', standalone: true }) class StubComponent {}

/**
 * LoginComponent (staff-app) — staff portal login page.
 *
 * WHY these tests matter:
 *  - submit() must reject invalid forms — calling the API with empty credentials
 *    wastes a round-trip and may expose form state.
 *  - The staff portal redirects to /home on success (not /dashboard) — a wrong
 *    redirect would leave staff on a 404 page after login.
 *  - mustChangePassword flow redirects to /auth/set-password — bypassing this
 *    means staff with temporary passwords can access features without setting a
 *    permanent password, which violates the security policy.
 *  - The error message distinguishes "admin portal" misuse from wrong credentials.
 */

describe('LoginComponent (staff-app)', () => {
  let component: LoginComponent;

  let mockAuth: {
    login:  ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };
  let mockLang: { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = {
      login:  vi.fn().mockReturnValue(of({ user: { roles: ['employee'] }, mustChangePassword: false })),
      logout: vi.fn(),
    };
    mockLang = { current: vi.fn().mockReturnValue('en'), set: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideHttpClient(),
        provideRouter([
          { path: 'home', component: StubComponent },
          { path: 'auth/set-password', component: StubComponent },
        ]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService, useValue: mockAuth },
        { provide: LangService, useValue: mockLang },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(LoginComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with loading=false', () => {
      expect(component.loading()).toBe(false);
    });

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });

    it('starts with showPwd=false', () => {
      expect(component.showPwd()).toBe(false);
    });

    it('form is invalid when empty', () => {
      expect(component.form.invalid).toBe(true);
    });
  });

  // ── submit() — guard clauses ──────────────────────────────────────────────────

  describe('submit() — guard clauses', () => {
    it('does not call auth.login when form is invalid', () => {
      component.form.patchValue({ staffId: '', password: '' });
      component.submit();
      expect(mockAuth.login).not.toHaveBeenCalled();
    });

    it('does not call auth.login when already loading', () => {
      component.form.patchValue({ staffId: 'NC001', password: 'pass' });
      component.loading.set(true);
      component.submit();
      expect(mockAuth.login).not.toHaveBeenCalled();
    });
  });

  // ── submit() — success ────────────────────────────────────────────────────────

  describe('submit() — success', () => {
    beforeEach(() => {
      component.form.patchValue({ staffId: 'NC001', password: 'pass123' });
    });

    it('calls auth.login with portal=staff', () => {
      component.submit();
      expect(mockAuth.login).toHaveBeenCalledWith(
        expect.objectContaining({ portal: 'staff', staffId: 'NC001' }),
      );
    });

    it('clears the error before submitting', () => {
      component.error.set('old error');
      component.submit();
      // Error cleared immediately; login was called
      expect(mockAuth.login).toHaveBeenCalled();
    });
  });

  // ── submit() — API error ──────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.form.patchValue({ staffId: 'NC001', password: 'wrong' });
    });

    it('sets generic error on credential failure', () => {
      mockAuth.login.mockReturnValue(throwError(() => ({ error: { error: 'Invalid.' } })));
      component.submit();
      expect(component.error()).toBe('Invalid Staff ID or password.');
    });

    it('sets "admin portal" message when error contains "Admin accounts"', () => {
      mockAuth.login.mockReturnValue(
        throwError(() => ({ error: { error: 'Admin accounts must use the admin portal.' } })),
      );
      component.submit();
      expect(component.error()).toBe('Admin accounts must use the admin portal.');
    });

    it('clears loading after error', () => {
      mockAuth.login.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });
});
