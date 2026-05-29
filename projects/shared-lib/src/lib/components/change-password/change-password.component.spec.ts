import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ChangePasswordComponent } from './change-password.component';
import { AuthService } from '../../auth/auth.service';

/**
 * ChangePasswordComponent (shared-lib) — reusable password-change form used by both
 * admin-app (Account page) and staff-app (Profile page).
 *
 * WHY these tests matter:
 *  - Whitespace-only passwords must be rejected before hitting the API — a user who
 *    accidentally types only spaces would set an unusable password that bypasses the
 *    minLength(8) validator (eight spaces pass the length check).
 *  - Mismatched passwords must be caught client-side to avoid a confusing server error.
 *  - success=true shows the "Password changed" confirmation; if not set, the user
 *    doesn't know the action succeeded.
 *  - saving flag must be cleared on both success and error so the button re-enables.
 */

describe('ChangePasswordComponent', () => {
  let component: ChangePasswordComponent;
  let mockAuth: { changePassword: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = { changePassword: vi.fn().mockReturnValue(of(undefined)) };

    await TestBed.configureTestingModule({
      imports: [ChangePasswordComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: AuthService, useValue: mockAuth },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(ChangePasswordComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with saving=false', () => {
      expect(component.saving()).toBe(false);
    });

    it('starts with success=false', () => {
      expect(component.success()).toBe(false);
    });

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });

    it('form is invalid when empty — required validators on all three fields', () => {
      expect(component.form.invalid).toBe(true);
    });
  });

  // ── password strength policy (8+ chars, uppercase, number) ──────────────────────

  describe('password requirements', () => {
    it('marks newPassword invalid when it lacks an uppercase letter or number', () => {
      component.form.get('newPassword')!.setValue('lowercaseonly');
      expect(component.form.get('newPassword')!.hasError('weakPassword')).toBe(true);
    });

    it('accepts a password with 8+ chars, an uppercase letter and a number', () => {
      component.form.get('newPassword')!.setValue('NewPass12');
      expect(component.form.get('newPassword')!.errors).toBeNull();
    });

    it('pwdChecks reflects which requirements are met as the user types', () => {
      component.form.get('newPassword')!.setValue('abc');           // too short, no upper, no num
      expect(component.pwdChecks).toEqual({ minLength: false, hasUpper: false, hasNumber: false });
      component.form.get('newPassword')!.setValue('Abcdefg9');      // all met
      expect(component.pwdChecks).toEqual({ minLength: true, hasUpper: true, hasNumber: true });
    });
  });

  // ── submit() — whitespace guard ───────────────────────────────────────────────

  describe('submit() — whitespace-only guard', () => {
    it('rejects whitespace-only currentPassword and shows error', () => {
      component.form.patchValue({ currentPassword: '   ', newPassword: 'ValidPwd1', confirmPassword: 'ValidPwd1' });
      component.submit();
      expect(component.error()).toBe('Password fields cannot be blank.');
      expect(mockAuth.changePassword).not.toHaveBeenCalled();
    });

    it('rejects whitespace-only newPassword and shows error', () => {
      component.form.patchValue({ currentPassword: 'OldPass1', newPassword: '        ', confirmPassword: '        ' });
      component.submit();
      expect(component.error()).toBe('Password fields cannot be blank.');
      expect(mockAuth.changePassword).not.toHaveBeenCalled();
    });

    it('rejects whitespace-only confirmPassword and shows error', () => {
      component.form.patchValue({ currentPassword: 'OldPass1', newPassword: 'NewPass1', confirmPassword: '   ' });
      component.submit();
      expect(component.error()).toBe('Password fields cannot be blank.');
      expect(mockAuth.changePassword).not.toHaveBeenCalled();
    });
  });

  // ── submit() — mismatch guard ─────────────────────────────────────────────────

  describe('submit() — password mismatch guard', () => {
    it('sets mismatch error when newPassword and confirmPassword differ', () => {
      component.form.patchValue({ currentPassword: 'OldPass1', newPassword: 'NewPass1', confirmPassword: 'Different1' });
      component.submit();
      expect(component.error()).toBe('New passwords do not match.');
      expect(mockAuth.changePassword).not.toHaveBeenCalled();
    });

    it('does not call API when form is invalid (e.g. minLength not met)', () => {
      component.form.patchValue({ currentPassword: 'old', newPassword: 'short', confirmPassword: 'short' });
      component.submit();
      expect(mockAuth.changePassword).not.toHaveBeenCalled();
    });

    it('does not call API when already saving', () => {
      component.form.patchValue({ currentPassword: 'OldPass1', newPassword: 'NewPass12', confirmPassword: 'NewPass12' });
      component.saving.set(true);
      component.submit();
      expect(mockAuth.changePassword).not.toHaveBeenCalled();
    });
  });

  // ── submit() — success ────────────────────────────────────────────────────────

  describe('submit() — success', () => {
    beforeEach(() => {
      component.form.patchValue({ currentPassword: 'OldPass1', newPassword: 'NewPass12', confirmPassword: 'NewPass12' });
    });

    it('calls auth.changePassword with the current and new passwords', () => {
      component.submit();
      expect(mockAuth.changePassword).toHaveBeenCalledWith({
        currentPassword: 'OldPass1',
        newPassword: 'NewPass12',
      });
    });

    it('sets success=true after a successful change', () => {
      component.submit();
      expect(component.success()).toBe(true);
    });

    it('clears saving flag after success', () => {
      component.submit();
      expect(component.saving()).toBe(false);
    });

    it('resets the form after success', () => {
      component.submit();
      expect(component.form.value.newPassword).toBeNull();
    });
  });

  // ── submit() — error ──────────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.form.patchValue({ currentPassword: 'OldPass1', newPassword: 'NewPass12', confirmPassword: 'NewPass12' });
      mockAuth.changePassword.mockReturnValue(
        throwError(() => ({ error: { error: 'Current password is incorrect.' } })),
      );
    });

    it('sets error from API response', () => {
      component.submit();
      expect(component.error()).toBe('Current password is incorrect.');
    });

    it('clears saving flag after error', () => {
      component.submit();
      expect(component.saving()).toBe(false);
    });

    it('falls back to generic error when no specific message', () => {
      mockAuth.changePassword.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.error()).toBe('Failed to change password.');
    });
  });
});
