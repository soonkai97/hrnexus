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
    http.expectOne('/api/auth/me/permissions').flush(['portal.admin', 'staff.view']);

    expect(service.has('portal.admin')).toBe(true);
    expect(service.has('staff.view')).toBe(true);
    expect(service.has('staff.manage')).toBe(false);
    expect(service.loaded()).toBe(true);
  });

  it('fails silently to an empty set on error but still marks loaded', () => {
    service.load().subscribe();
    http.expectOne('/api/auth/me/permissions').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.has('portal.admin')).toBe(false);
    expect(service.loaded()).toBe(true);
  });

  it('clear() empties the set and resets loaded', () => {
    service.load().subscribe();
    http.expectOne('/api/auth/me/permissions').flush(['portal.admin']);

    service.clear();
    expect(service.has('portal.admin')).toBe(false);
    expect(service.loaded()).toBe(false);
  });
});
