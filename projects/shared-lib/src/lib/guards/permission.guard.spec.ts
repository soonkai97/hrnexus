import { TestBed } from '@angular/core/testing';
import { Observable, firstValueFrom, of } from 'rxjs';
import { Router, UrlTree } from '@angular/router';
import { permissionGuard } from './permission.guard';
import { PermissionService } from '../services/permission.service';

describe('permissionGuard', () => {
  let mockPerms:  { loaded: ReturnType<typeof vi.fn>; has: ReturnType<typeof vi.fn>; load: ReturnType<typeof vi.fn> };
  let mockRouter: { createUrlTree: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    mockPerms  = { loaded: vi.fn().mockReturnValue(true), has: vi.fn().mockReturnValue(true), load: vi.fn().mockReturnValue(of(void 0)) };
    mockRouter = { createUrlTree: vi.fn().mockReturnValue({} as UrlTree) };
    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionService, useValue: mockPerms },
        { provide: Router, useValue: mockRouter },
      ],
    });
  });

  const exec = (key: any, fallback?: string) =>
    TestBed.runInInjectionContext(() => permissionGuard(key, fallback)(null as any, null as any));

  it('allows access when loaded and the permission is held', () => {
    expect(exec('staff.view')).toBe(true);
  });

  it('redirects to the fallback when loaded and the permission is missing', () => {
    mockPerms.has.mockReturnValue(false);
    exec('roles.manage', '/dashboard');
    expect(mockRouter.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
  });

  it('loads permissions first on a fresh page load, then decides', async () => {
    mockPerms.loaded.mockReturnValue(false);
    mockPerms.has.mockReturnValue(true);

    const result = exec('staff.view') as Observable<boolean | UrlTree>;
    const value  = await firstValueFrom(result);

    expect(mockPerms.load).toHaveBeenCalled();
    expect(value).toBe(true);
  });
});
