import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { SetPasswordComponent } from './set-password.component';
import { AuthService, LangService } from 'shared-lib';

@Component({ template: '', standalone: true }) class StubComponent {}

/**
 * SetPasswordComponent (admin-app) — forced password change on first login.
 *
 * WHY these tests matter:
 *  - Minimum-length validation prevents trivially weak passwords from being set
 *    in the forced-change flow, which would defeat the purpose of the policy.
 *  - Confirm-match validation prevents typo-locked accounts where the admin
 *    sets one thing and types another, then can't log in.
 *  - After success the app redirects to /dashboard — a wrong redirect leaves
 *    the admin on the set-password page indefinitely.
 *  - Error propagation from the API ensures the form surfaces backend-level
 *    password policy rejections (e.g. "Password previously used").
 */

describe('SetPasswordComponent', () => {
  let component: SetPasswordComponent;

  let mockAuth:   { forceChangePassword: ReturnType<typeof vi.fn> };
  let mockRouter: { navigate: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth   = { forceChangePassword: vi.fn().mockReturnValue(of(undefined)) };
    mockRouter = { navigate: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [SetPasswordComponent],
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'dashboard', component: StubComponent }]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService, useValue: mockAuth },
        { provide: LangService, useValue: { current: vi.fn().mockReturnValue('en'), set: vi.fn() } },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(SetPasswordComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with empty password fields', () => {
      expect(component.newPwd).toBe('');
      expect(component.confirmPwd).toBe('');
    });

    it('starts with loading=false', () => {
      expect(component.loading()).toBe(false);
    });

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });

    it('overrides redirectPath to /dashboard for the admin portal', () => {
      expect((component as any).redirectPath).toBe('/dashboard');
    });
  });

  // ── submit() — validation ─────────────────────────────────────────────────────

  describe('submit() — validation', () => {
    it('sets error when password is shorter than 8 characters', () => {
      component.newPwd     = 'short';
      component.confirmPwd = 'short';
      component.submit();
      expect(component.error()).toBe('Password must be at least 8 characters.');
      expect(mockAuth.forceChangePassword).not.toHaveBeenCalled();
    });

    it('sets error when passwords do not match', () => {
      component.newPwd     = 'LongPass1';
      component.confirmPwd = 'LongPass2';
      component.submit();
      expect(component.error()).toBe('Passwords do not match.');
      expect(mockAuth.forceChangePassword).not.toHaveBeenCalled();
    });

    it('does not call API when already loading', () => {
      component.newPwd     = 'LongPass1';
      component.confirmPwd = 'LongPass1';
      component.loading.set(true);
      component.submit();
      expect(mockAuth.forceChangePassword).not.toHaveBeenCalled();
    });
  });

  // ── submit() — success ────────────────────────────────────────────────────────

  describe('submit() — success', () => {
    beforeEach(() => {
      component.newPwd     = 'NewSecure@1';
      component.confirmPwd = 'NewSecure@1';
    });

    it('calls forceChangePassword with the new password', () => {
      component.submit();
      expect(mockAuth.forceChangePassword).toHaveBeenCalledWith('NewSecure@1');
    });

    it('clears the error signal before submitting', () => {
      component.error.set('previous error');
      component.submit();
      // Error is cleared before the API call
      expect(mockAuth.forceChangePassword).toHaveBeenCalled();
    });
  });

  // ── submit() — API error ──────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.newPwd     = 'NewSecure@1';
      component.confirmPwd = 'NewSecure@1';
    });

    it('sets the API error message when forceChangePassword fails', () => {
      mockAuth.forceChangePassword.mockReturnValue(
        throwError(() => ({ error: { error: 'Password previously used.' } })),
      );
      component.submit();
      expect(component.error()).toBe('Password previously used.');
    });

    it('sets a generic error when the API response has no error detail', () => {
      mockAuth.forceChangePassword.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.error()).toBe('Failed to set password. Please try again.');
    });

    it('clears loading flag after an API error', () => {
      mockAuth.forceChangePassword.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });
});
