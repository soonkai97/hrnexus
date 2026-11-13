import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { LoginComponent } from './login.component';
import { AuthService } from 'shared-lib';

@Component({ template: '', standalone: true }) class StubComponent {}

/**
 * LoginComponent (staff-app) — staff-app login page.
 *
 * WHY these tests matter:
 *  - submit() must reject invalid forms — calling the API with empty credentials
 *    wastes a round-trip and may expose form state.
 *  - The staff app redirects to /home on success (not /dashboard) — a wrong
 *    redirect would leave staff on a 404 page after login.
 *  - Any active account (admin or employee) may sign in here; the request carries no portal field.
 */

describe('LoginComponent (staff-app)', () => {
  let component: LoginComponent;

  let mockAuth: {
    login:  ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    mockAuth = {
      login:  vi.fn().mockReturnValue(of({ user: { roles: ['employee'] } })),
      logout: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideHttpClient(),
        provideRouter([
          { path: 'home', component: StubComponent },
        ]),
        { provide: AuthService, useValue: mockAuth },
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

    it('does not call auth.login and shows error when staffId is whitespace-only', () => {
      component.form.patchValue({ staffId: '   ', password: 'pass123' });
      component.submit();
      expect(mockAuth.login).not.toHaveBeenCalled();
      expect(component.error()).toBe('Please enter your Staff ID.');
    });
  });

  // ── submit() — success ────────────────────────────────────────────────────────

  describe('submit() — success', () => {
    beforeEach(() => {
      component.form.patchValue({ staffId: 'NC001', password: 'pass123' });
    });

    it('calls auth.login with only the staff ID and password', () => {
      component.submit();
      expect(mockAuth.login).toHaveBeenCalledWith({ staffId: 'NC001', password: 'pass123' });
    });

    it('uppercases the Staff ID before submitting (stored uppercase)', () => {
      component.form.patchValue({ staffId: ' nc001 ', password: 'pass123' });
      component.submit();
      expect(mockAuth.login).toHaveBeenCalledWith(
        expect.objectContaining({ staffId: 'NC001' }),
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

    it('clears loading after error', () => {
      mockAuth.login.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });
});
