import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PermissionAdminService, groupedPermissions } from './permission-admin.service';

/**
 * PermissionAdminService — admin CRUD for the permission framework (catalog, role defaults,
 * per-user overrides). WHY these tests matter: the role/user permission editors are the only
 * way to change who can do what, so a wrong URL or body silently corrupts access control. The
 * role name is URL-encoded because role names can contain spaces / non-ASCII.
 */
describe('PermissionAdminService', () => {
  let service: PermissionAdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PermissionAdminService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PermissionAdminService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('catalog() GETs the permission catalog', () => {
    service.catalog().subscribe(r => expect(r).toEqual(['staff.view']));
    const req = http.expectOne('/api/permissions');
    expect(req.request.method).toBe('GET');
    req.flush(['staff.view']);
  });

  it('getRolePermissions() GETs the role, URL-encoding the name', () => {
    service.getRolePermissions('FLOOR STAFF').subscribe();
    const req = http.expectOne('/api/permissions/roles/FLOOR%20STAFF');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('setRolePermissions() PUTs the permissions array in a { permissions } body', () => {
    service.setRolePermissions('MANAGER', ['staff.view', 'reports.view']).subscribe();
    const req = http.expectOne('/api/permissions/roles/MANAGER');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ permissions: ['staff.view', 'reports.view'] });
    req.flush(null);
  });

  it('getUserOverrides() GETs the per-user overrides', () => {
    service.getUserOverrides('emp-1').subscribe();
    const req = http.expectOne('/api/permissions/users/emp-1');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('setUserOverrides() PUTs the overrides in an { overrides } body', () => {
    const ov = [{ permissionKey: 'staff.manage', granted: true }];
    service.setUserOverrides('emp-1', ov).subscribe();
    const req = http.expectOne('/api/permissions/users/emp-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ overrides: ov });
    req.flush(null);
  });
});

/**
 * groupedPermissions — projects a flat set of visible keys onto the ordered display groups.
 * WHY: the editors render these groups; an unknown key must never be silently dropped (that would
 * hide a real permission from the admin), and empty groups must collapse so the UI stays clean.
 */
describe('groupedPermissions', () => {
  it('keeps only visible keys, in group order, dropping empty groups', () => {
    const groups = groupedPermissions(['portal.admin', 'staff.view', 'payroll.view']);
    expect(groups.map(g => g.label)).toEqual([
      'Access',
      'Operations',
      'Finance',
    ]);
    expect(groups[1]!.keys).toEqual(['staff.view']); // setup group has no visible key → dropped
  });

  it('appends any unknown key to a trailing "other" group so nothing is hidden', () => {
    const groups = groupedPermissions(['staff.view', 'some.future.key']);
    const other = groups.find(g => g.label === 'Other');
    expect(other?.keys).toEqual(['some.future.key']);
  });

  it('returns an empty array when nothing is visible', () => {
    expect(groupedPermissions([])).toEqual([]);
  });
});
