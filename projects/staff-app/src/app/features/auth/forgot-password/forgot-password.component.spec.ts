import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ForgotPasswordComponent } from './forgot-password.component';
import { AuthService, LangService } from 'shared-lib';

/**
 * ForgotPasswordComponent (staff-app) — self-service password reset request.
 *
 * WHY these tests matter:
 *  - submit() must not fire with an empty staffId — prevents accidental or
 *    automated enumeration of existing accounts.
 *  - sent=true shows the "check your email" confirmation; if the signal isn't
 *    set after a successful found=true response, staff see no feedback.
 *  - found=false must show an error, not a success banner — otherwise staff
 *    with a typo in their ID think the reset was sent when it wasn't.
 */

describe('ForgotPasswordComponent (staff-app)', () => {
  let component: ForgotPasswordComponent;

  let mockAuth: { forgotPassword: ReturnType<typeof vi.fn> };
  let mockLang: { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = { forgotPassword: vi.fn().mockReturnValue(of({ found: true })) };
    mockLang = { current: vi.fn().mockReturnValue('en'), set: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService, useValue: mockAuth },
        { provide: LangService, useValue: mockLang },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(ForgotPasswordComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with empty staffId', () => {
      expect(component.staffId).toBe('');
    });

    it('starts with sent=false', () => {
      expect(component.sent()).toBe(false);
    });

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });
  });

  // ── submit() — guard clauses ──────────────────────────────────────────────────

  describe('submit() — guard clauses', () => {
    it('does not call forgotPassword with empty staffId', () => {
      component.staffId = '';
      component.submit();
      expect(mockAuth.forgotPassword).not.toHaveBeenCalled();
    });

    it('does not call forgotPassword with whitespace-only staffId', () => {
      component.staffId = '   ';
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

  // ── submit() — found=true ─────────────────────────────────────────────────────

  describe('submit() — found=true', () => {
    beforeEach(() => { component.staffId = 'NC001'; });

    it('calls forgotPassword with the trimmed, uppercased staffId', () => {
      component.staffId = '  nc001  ';
      component.submit();
      expect(mockAuth.forgotPassword).toHaveBeenCalledWith('NC001');
    });

    it('sets sent=true so the success confirmation is shown', () => {
      component.submit();
      expect(component.sent()).toBe(true);
    });

    it('clears loading after a found response', () => {
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });

  // ── submit() — found=false ────────────────────────────────────────────────────

  describe('submit() — found=false', () => {
    beforeEach(() => {
      component.staffId = 'NC999';
      mockAuth.forgotPassword.mockReturnValue(of({ found: false }));
    });

    it('sets not-found error when staff ID is not recognised', () => {
      component.submit();
      expect(component.error()).toBe('Staff ID not found. Please check and try again.');
    });

    it('keeps sent=false when not found', () => {
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

    it('sets a generic error message', () => {
      component.submit();
      expect(component.error()).toBe('Something went wrong. Please try again.');
    });

    it('clears loading', () => {
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });
});
