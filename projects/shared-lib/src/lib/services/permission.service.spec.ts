import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PermissionService } from './permission.service';

describe('PermissionService', () => {
  let service: PermissionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PermissionService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PermissionService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads effective permissions and answers has()', () => {
    service.load().subscribe();
    http.expectOne('/api/auth/me/permissions').flush(['roles.manage', 'staff.view']);

    expect(service.has('roles.manage')).toBe(true);
    expect(service.has('staff.view')).toBe(true);
    expect(service.has('staff.manage')).toBe(false);
    expect(service.loaded()).toBe(true);
  });

  it('fails silently to an empty set on error but still marks loaded', () => {
    service.load().subscribe();
    http.expectOne('/api/auth/me/permissions').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.has('staff.view')).toBe(false);
    expect(service.loaded()).toBe(true);
  });

  it('clear() empties the set and resets loaded', () => {
    service.load().subscribe();
    http.expectOne('/api/auth/me/permissions').flush(['staff.view']);

    service.clear();
    expect(service.has('staff.view')).toBe(false);
    expect(service.loaded()).toBe(false);
  });
});
