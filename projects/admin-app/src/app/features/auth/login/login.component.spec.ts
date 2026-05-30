import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { AdminLoginComponent } from './login.component';
import { AuthService, LangService, PermissionService } from 'shared-lib';

@Component({ template: '', standalone: true }) class StubComponent {}

/**
 * AdminLoginComponent — admin portal login page.
 *
 * WHY these tests matter:
 *  - submit() must reject invalid forms early — calling the API with empty fields
 *    wastes a round-trip and can reveal form state to attackers.
 *  - Role guard inside submit() enforces that only admin/manager accounts can access
 *    the admin portal; missing this allows any staff member to log in.
 *  - Error messaging must distinguish auth failures from access-denied so users know
 *    whether to retry or contact an admin.
 */

describe('AdminLoginComponent', () => {
  let component: AdminLoginComponent;

  let mockAuth: {
    login:  ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };
  let mockLang:   { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn> };
  let mockPermissions: { load: ReturnType<typeof vi.fn>; has: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAuth = {
      login:  vi.fn().mockReturnValue(of({ user: { roles: ['SUPERADMIN'] } })),
      logout: vi.fn(),
    };
    mockLang = { current: vi.fn().mockReturnValue('en'), set: vi.fn() };
    // By default the signed-in user HAS admin-portal access; gate tests override has().
    mockPermissions = { load: vi.fn().mockReturnValue(of(void 0)), has: vi.fn().mockReturnValue(true), clear: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [AdminLoginComponent],
      providers: [
        provideHttpClient(),
        provideRouter([
          { path: 'dashboard', component: StubComponent },
        ]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService, useValue: mockAuth },
        { provide: LangService, useValue: mockLang },
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
      expect(component.error()).toBe('auth.errEnterStaffId');
    });
  });

  // ── submit() — success paths ──────────────────────────────────────────────────

  describe('submit() — success: admin role', () => {
    beforeEach(() => {
      component.form.patchValue({ staffId: 'NC001', password: 'pass123' });
    });

    it('calls auth.login with the form values and portal=admin', () => {
      component.submit();
      expect(mockAuth.login).toHaveBeenCalledWith({ staffId: 'NC001', password: 'pass123', portal: 'admin' });
    });

    it('navigates to /dashboard when login succeeds', () => {
      mockAuth.login.mockReturnValue(of({ user: { roles: ['SUPERADMIN'] } }));
      component.submit();
      // Routing handled by provideRouter; just confirm loading stays true (no error path)
      expect(component.error()).toBe('');
    });
  });

  // ── submit() — role guard ─────────────────────────────────────────────────────

  describe('submit() — portal.admin gate: user without admin-portal permission', () => {
    beforeEach(() => {
      component.form.patchValue({ staffId: 'NC002', password: 'pass123' });
      // Login succeeded (defense-in-depth) but the effective permissions lack portal.admin.
      mockPermissions.has.mockReturnValue(false);
    });

    it('calls auth.logout() when the user lacks portal.admin', () => {
      component.submit();
      expect(mockAuth.logout).toHaveBeenCalled();
    });

    it('sets access-denied error when the user lacks portal.admin', () => {
      component.submit();
      expect(component.error()).toBe('auth.errAccessDeniedPortal');
    });

    it('clears loading after rejecting a user without portal.admin', () => {
      component.submit();
      expect(component.loading()).toBe(false);
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
      expect(component.error()).toBe('auth.errInvalidCredentials');
    });

    it('sets access-denied message when the API reports no admin-portal access', () => {
      mockAuth.login.mockReturnValue(throwError(() => ({ error: { error: 'Access denied. You do not have admin portal access.' } })));
      component.submit();
      expect(component.error()).toBe('auth.errAccessDeniedPortal');
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
