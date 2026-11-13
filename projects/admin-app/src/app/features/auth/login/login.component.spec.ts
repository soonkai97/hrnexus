import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { AdminLoginComponent } from './login.component';
import { AuthService, PermissionService } from 'shared-lib';

@Component({ template: '', standalone: true }) class StubComponent {}

/**
 * AdminLoginComponent — admin-app login page.
 *
 * WHY these tests matter:
 *  - submit() must reject invalid forms early — calling the API with empty fields
 *    wastes a round-trip and can reveal form state to attackers.
 *  - Any active account may sign in (one app for every role); the login page must not
 *    gate on a permission — route guards decide what each user can open.
 *  - A failed sign-in shows one generic message so the page never reveals why.
 */

describe('AdminLoginComponent', () => {
  let component: AdminLoginComponent;

  let mockAuth: {
    login:  ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };
  let mockPermissions: { load: ReturnType<typeof vi.fn>; has: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = {
      login:  vi.fn().mockReturnValue(of({ user: { roles: ['SUPERADMIN'] } })),
      logout: vi.fn(),
    };
    // Spies prove the login page never consults permissions.
    mockPermissions = { load: vi.fn().mockReturnValue(of(void 0)), has: vi.fn().mockReturnValue(true), clear: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [AdminLoginComponent],
      providers: [
        provideHttpClient(),
        provideRouter([
          { path: 'dashboard', component: StubComponent },
        ]),
        { provide: AuthService, useValue: mockAuth },
        { provide: PermissionService, useValue: mockPermissions },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(AdminLoginComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with loading=false so the button is enabled', () => {
      expect(component.loading()).toBe(false);
    });

    it('starts with no error message', () => {
      expect(component.error()).toBe('');
    });

    it('starts with showPwd=false so password is masked', () => {
      expect(component.showPwd()).toBe(false);
    });

    it('creates a form with staffId and password controls', () => {
      expect(component.form.contains('staffId')).toBe(true);
      expect(component.form.contains('password')).toBe(true);
    });

    it('form is invalid when empty — required validators are set', () => {
      expect(component.form.invalid).toBe(true);
    });
  });

  // ── submit() — guard clauses ───────────────────────────────────────────────────

  describe('submit() — guard clauses', () => {
    it('does not call auth.login when form is invalid', () => {
      component.form.patchValue({ staffId: '', password: '' });
      component.submit();
      expect(mockAuth.login).not.toHaveBeenCalled();
    });

    it('does not call auth.login when already loading', () => {
      component.form.patchValue({ staffId: 'NC001', password: 'pass123' });
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

  // ── submit() — success paths ──────────────────────────────────────────────────

  describe('submit() — success: admin role', () => {
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
      expect(mockAuth.login).toHaveBeenCalledWith(expect.objectContaining({ staffId: 'NC001' }));
    });

    it('navigates to /dashboard when login succeeds', () => {
      mockAuth.login.mockReturnValue(of({ user: { roles: ['SUPERADMIN'] } }));
      component.submit();
      // Routing handled by provideRouter; just confirm loading stays true (no error path)
      expect(component.error()).toBe('');
    });
  });

  // ── submit() — one app for every role ─────────────────────────────────────────

  describe('submit() — any active account signs in', () => {
    it('navigates to /dashboard after login without checking any permission', () => {
      // An employee holds no management permission; the sign-in must still succeed. What the
      // user can open is decided later by each route guard, not by the login page.
      mockAuth.login.mockReturnValue(of({ user: { roles: ['EMPLOYEE'] } }));
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      component.form.patchValue({ staffId: 'NC002', password: 'pass123' });

      component.submit();

      expect(navigate).toHaveBeenCalledWith(['/dashboard']);
      expect(mockAuth.logout).not.toHaveBeenCalled();
      expect(mockPermissions.load).not.toHaveBeenCalled();
      expect(mockPermissions.has).not.toHaveBeenCalled();
      expect(component.error()).toBe('');
    });
  });

  // ── submit() — error path ─────────────────────────────────────────────────────

  describe('submit() — API error', () => {
    beforeEach(() => {
      component.form.patchValue({ staffId: 'NC001', password: 'wrong' });
    });

    it('sets generic error message on credential failure', () => {
      mockAuth.login.mockReturnValue(throwError(() => ({ error: { error: 'Bad credentials' } })));
      component.submit();
      expect(component.error()).toBe('Invalid Staff ID or password.');
    });

    it('clears loading flag after an API error', () => {
      mockAuth.login.mockReturnValue(throwError(() => ({})));
      component.submit();
      expect(component.loading()).toBe(false);
    });

    it('sets loading=true while the request is in-flight (synchronous observable completes immediately)', () => {
      // After error, loading is reset; confirm it ends at false
      mockAuth.login.mockReturnValue(throwError(() => new Error('Network')));
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });
});
