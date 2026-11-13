import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ForgotPasswordComponent } from './forgot-password.component';
import { AuthService } from 'shared-lib';

/**
 * ForgotPasswordComponent (admin-app) — self-service password reset request.
 *
 * WHY these tests matter:
 *  - submit() must not fire when staffId is empty — an empty API call could
 *    expose account-enumeration information or cause backend errors.
 *  - sent signal controls the success confirmation UI; if it stays false after
 *    a successful API call the user sees no feedback and retries unnecessarily.
 *  - When the staffId is not found the component must surface the error rather
 *    than showing a success state (which would confuse the user).
 *  - Generic API errors must be surfaced so admins know to contact IT support.
 */

describe('ForgotPasswordComponent', () => {
  let component: ForgotPasswordComponent;

  let mockAuth: { forgotPassword: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = { forgotPassword: vi.fn().mockReturnValue(of({ found: true })) };

    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: AuthService, useValue: mockAuth },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(ForgotPasswordComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with an empty staffId', () => {
      expect(component.staffId).toBe('');
    });

    it('starts with loading=false', () => {
      expect(component.loading()).toBe(false);
    });

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });

    it('starts with sent=false so the form is shown', () => {
      expect(component.sent()).toBe(false);
    });
  });

  // ── submit() — guard clauses ──────────────────────────────────────────────────

  describe('submit() — guard clauses', () => {
    it('does not call forgotPassword when staffId is empty', () => {
      component.staffId = '';
      component.submit();
      expect(mockAuth.forgotPassword).not.toHaveBeenCalled();
    });

    it('does not call forgotPassword when already loading', () => {
      component.staffId = 'NC001';
      component.loading.set(true);
      component.submit();
      expect(mockAuth.forgotPassword).not.toHaveBeenCalled();
    });
  });

  // ── submit() — success: staff found ──────────────────────────────────────────

  describe('submit() — success: found=true', () => {
    beforeEach(() => {
      component.staffId = 'NC001';
    });

    it('calls forgotPassword with the trimmed, uppercased staffId', () => {
      component.staffId = '  nc001  ';
      component.submit();
      expect(mockAuth.forgotPassword).toHaveBeenCalledWith('NC001');
    });

    it('sets sent=true when the staff ID is found', () => {
      mockAuth.forgotPassword.mockReturnValue(of({ found: true }));
      component.submit();
      expect(component.sent()).toBe(true);
    });

    it('clears loading after a successful found response', () => {
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });

  // ── submit() — success: staff NOT found ──────────────────────────────────────

  describe('submit() — success: found=false', () => {
    beforeEach(() => {
      component.staffId = 'NC999';
      mockAuth.forgotPassword.mockReturnValue(of({ found: false }));
    });

    it('sets an error message when the staff ID is not found', () => {
      component.submit();
      expect(component.error()).toBe('Staff ID not found. Please check and try again.');
    });

    it('does NOT set sent=true when the staff ID is not found', () => {
      component.submit();
      expect(component.sent()).toBe(false);
    });
  });

  // ── submit() — API error ──────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.staffId = 'NC001';
      mockAuth.forgotPassword.mockReturnValue(throwError(() => new Error('Network')));
    });

    it('sets a generic error message on API failure', () => {
      component.submit();
      expect(component.error()).toBe('Something went wrong. Please try again.');
    });

    it('clears loading after an API error', () => {
      component.submit();
      expect(component.loading()).toBe(false);
    });

    it('does NOT set sent=true on API failure', () => {
      component.submit();
      expect(component.sent()).toBe(false);
    });
  });
});
