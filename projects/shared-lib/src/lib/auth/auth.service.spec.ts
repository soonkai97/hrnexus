import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { AuthUser } from '../models';

function fakeUser(roles: string[]): AuthUser {
  return { id: 'u1', staffId: 'NC001', fullName: 'Test User', email: 't@test.com', roles };
}

describe('AuthService — userRole and hasRole', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  // ── userRole ──────────────────────────────────────────────────────────────

  it('is null when no user is logged in', () => {
    expect(service.userRole()).toBeNull();
  });

  it('resolves to employee for any non-admin/non-manager role', () => {
    // A bartender, server, etc. has job-specific roles that aren't admin/manager
    (service as any)._user.set(fakeUser(['BARTENDER']));
    expect(service.userRole()).toBe('employee');
  });

  it('resolves to manager when roles contains manager', () => {
    (service as any)._user.set(fakeUser(['MANAGER']));
    expect(service.userRole()).toBe('manager');
  });

  it('resolves to admin when roles contains admin', () => {
    (service as any)._user.set(fakeUser(['ADMIN']));
    expect(service.userRole()).toBe('admin');
  });

  it('admin takes precedence over manager when staff holds both roles', () => {
    // Highest privilege wins — access decisions must not accidentally downgrade
    (service as any)._user.set(fakeUser(['MANAGER', 'ADMIN']));
    expect(service.userRole()).toBe('admin');
  });

  it('resolves to null for an empty roles array', () => {
    (service as any)._user.set(fakeUser([]));
    expect(service.userRole()).toBeNull();
  });

  // ── hasRole ───────────────────────────────────────────────────────────────

  it('returns false for all roles when not logged in', () => {
    expect(service.hasRole('admin')).toBe(false);
    expect(service.hasRole('manager')).toBe(false);
    expect(service.hasRole('employee')).toBe(false);
  });

  it('returns true only for the resolved permission level', () => {
    (service as any)._user.set(fakeUser(['MANAGER']));
    expect(service.hasRole('manager')).toBe(true);
    expect(service.hasRole('admin')).toBe(false);
    expect(service.hasRole('employee')).toBe(false);
  });

  it('returns true when one of multiple supplied roles matches', () => {
    // Admin and manager UIs both call hasRole('admin', 'manager') to gate shared views
    (service as any)._user.set(fakeUser(['MANAGER']));
    expect(service.hasRole('admin', 'manager')).toBe(true);
  });

  it('returns false when none of multiple supplied roles match', () => {
    (service as any)._user.set(fakeUser(['BARTENDER']));
    expect(service.hasRole('admin', 'manager')).toBe(false);
  });

  // ── isAuthenticated ───────────────────────────────────────────────────────

  it('is false when user signal is null', () => {
    expect(service.isAuthenticated()).toBe(false);
  });

  it('is true once a user is loaded into the signal', () => {
    (service as any)._user.set(fakeUser(['BARTENDER']));
    expect(service.isAuthenticated()).toBe(true);
  });
});
