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
 * SetPasswordComponent (staff-app) — forced password change on first login.
 *
 * WHY these tests matter:
 *  - The staff-app variant redirects to /home on success (not /dashboard) — a
 *    wrong redirect path confuses staff after they set their password.
 *  - Validation prevents weak or mismatched passwords from being accepted,
 *    protecting accounts that may have been issued with a temporary credential.
 *  - Error propagation surfaces backend rejections (e.g. "previously used").
 */

describe('SetPasswordComponent (staff-app)', () => {
  let component: SetPasswordComponent;

  let mockAuth: { forceChangePassword: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = { forceChangePassword: vi.fn().mockReturnValue(of(undefined)) };

    await TestBed.configureTestingModule({
      imports: [SetPasswordComponent],
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'home', component: StubComponent }]),
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

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });

    it('overrides redirectPath to /home for the staff portal', () => {
      expect((component as any).redirectPath).toBe('/home');
    });
  });

  // ── submit() — validation ─────────────────────────────────────────────────────

  describe('submit() — validation', () => {
    it('sets error when password is shorter than 8 characters', () => {
      component.newPwd     = 'short';
      component.confirmPwd = 'short';
      component.submit();
      expect(component.error()).toBe('Password must be at least 8 characters.');
    });

    it('sets error when passwords do not match', () => {
      component.newPwd     = 'LongPass1';
      component.confirmPwd = 'LongPass2';
      component.submit();
      expect(component.error()).toBe('Passwords do not match.');
    });

    it('does not call forceChangePassword on validation failure', () => {
      component.newPwd     = 'short';
      component.confirmPwd = 'short';
      component.submit();
      expect(mockAuth.forceChangePassword).not.toHaveBeenCalled();
    });
  });

  // ── submit() — success ────────────────────────────────────────────────────────

  describe('submit() — success', () => {
    it('calls forceChangePassword with the new password', () => {
      component.newPwd     = 'MyNewPass@1';
      component.confirmPwd = 'MyNewPass@1';
      component.submit();
      expect(mockAuth.forceChangePassword).toHaveBeenCalledWith('MyNewPass@1');
    });
  });

  // ── submit() — API error ──────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.newPwd     = 'MyNewPass@1';
      component.confirmPwd = 'MyNewPass@1';
    });

    it('sets error from the API response', () => {
      mockAuth.forceChangePassword.mockReturnValue(
        throwError(() => ({ error: { error: 'Password previously used.' } })),
      );
      component.submit();
      expect(component.error()).toBe('Password previously used.');
    });

    it('clears loading after an API error', () => {
      mockAuth.forceChangePassword.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });
});
