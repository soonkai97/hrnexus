import { test, expect, type APIRequestContext } from '@playwright/test';
import { ADMIN, STAFF } from '../shared/helpers';

/**
 * Permission-framework E2E — exercised through the real HTTP API (the backend is the actual
 * enforcement boundary; the FE guards are defence-in-depth on top). Proves:
 *  - the admin-portal LOGIN GATE (portal.admin),
 *  - per-user GRANT/REVOKE overrides take effect IMMEDIATELY (DB-resolved, not baked into the JWT),
 *  - a policy-protected endpoint returns 403 without the permission and 200 with it,
 *  - the effective-permissions endpoint reflects the override.
 *
 * Uses the seeded HRADMIN (SUPERADMIN, has every permission) as the operator and TESTSTAFF01
 * (EMPLOYEE, created by global-setup) as the subject. afterAll resets TESTSTAFF01's overrides so
 * the staff-portal specs still see a plain EMPLOYEE.
 */

const adminLogin = (request: APIRequestContext) =>
  request.post('/api/auth/login', { data: { staffId: ADMIN.staffId, password: ADMIN.password, portal: 'admin' } });

const employeeAdminLogin = (request: APIRequestContext) =>
  request.post('/api/auth/login', { data: { staffId: STAFF.staffId, password: STAFF.password, portal: 'admin' } });

async function adminToken(request: APIRequestContext): Promise<string> {
  const res = await adminLogin(request);
  expect(res.ok(), 'HRADMIN admin login should succeed').toBeTruthy();
  return (await res.json()).accessToken as string;
}

async function findEmployeeId(request: APIRequestContext, token: string): Promise<string> {
  const res = await request.get('/api/staff?page=1&pageSize=100', { headers: { Authorization: `Bearer ${token}` } });
  expect(res.ok()).toBeTruthy();
  const body = await res.json() as { items?: { id: string; staffId: string }[]; data?: { id: string; staffId: string }[] };
  const items = body.items ?? body.data ?? (body as unknown as { id: string; staffId: string }[]);
  const found = items.find(s => s.staffId === STAFF.staffId);
  expect(found, `${STAFF.staffId} should exist (created by global-setup)`).toBeTruthy();
  return found!.id;
}

function setOverrides(
  request: APIRequestContext, token: string, empId: string,
  overrides: { permissionKey: string; granted: boolean }[],
) {
  return request.put(`/api/permissions/users/${empId}`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { overrides },
  });
}

test.describe('Admin — permission framework', () => {
  let token = '';
  let empId = '';

  test.beforeAll(async ({ request }) => {
    token = await adminToken(request);
    empId = await findEmployeeId(request, token);
    await setOverrides(request, token, empId, []);   // clean slate
  });

  test.afterAll(async ({ request }) => {
    // Reset so other specs (and live use) see TESTSTAFF01 as a plain EMPLOYEE again.
    const t = await adminToken(request);
    const id = await findEmployeeId(request, t);
    await setOverrides(request, t, id, []);
  });

  test('admin-portal login gate: SUPERADMIN allowed, EMPLOYEE rejected', async ({ request }) => {
    const admin = await adminLogin(request);
    expect(admin.ok()).toBeTruthy();

    const emp = await employeeAdminLogin(request);
    expect(emp.ok(), 'a plain EMPLOYEE must not be able to sign into the admin portal').toBeFalsy();
  });

  test('a per-user GRANT override unlocks admin login immediately; revoking re-locks it', async ({ request }) => {
    // Grant portal.admin to the employee…
    expect((await setOverrides(request, token, empId, [{ permissionKey: 'portal.admin', granted: true }])).ok()).toBeTruthy();
    const afterGrant = await employeeAdminLogin(request);
    expect(afterGrant.ok(), 'granting portal.admin should let the employee in on the very next login').toBeTruthy();

    // …then clear it again.
    expect((await setOverrides(request, token, empId, [])).ok()).toBeTruthy();
    const afterClear = await employeeAdminLogin(request);
    expect(afterClear.ok(), 'clearing the grant should lock the employee back out immediately').toBeFalsy();
  });

  test('a policy-protected endpoint is 403 without the permission, 200 with it', async ({ request }) => {
    // Grant ONLY portal.admin so the employee can authenticate to admin, but NOT roles.manage.
    await setOverrides(request, token, empId, [{ permissionKey: 'portal.admin', granted: true }]);
    const empToken = (await (await employeeAdminLogin(request)).json()).accessToken as string;

    // GET /api/permissions (the catalog) requires roles.manage.
    const forbidden = await request.get('/api/permissions', { headers: { Authorization: `Bearer ${empToken}` } });
    expect(forbidden.status(), 'employee without roles.manage should get 403').toBe(403);

    const allowed = await request.get('/api/permissions', { headers: { Authorization: `Bearer ${token}` } });
    expect(allowed.ok(), 'HRADMIN has roles.manage → 200').toBeTruthy();

    await setOverrides(request, token, empId, []);
  });

  test('effective-permissions endpoint reflects the granted override', async ({ request }) => {
    await setOverrides(request, token, empId, [{ permissionKey: 'portal.admin', granted: true }]);
    const empToken = (await (await employeeAdminLogin(request)).json()).accessToken as string;

    const res = await request.get('/api/auth/me/permissions', { headers: { Authorization: `Bearer ${empToken}` } });
    expect(res.ok()).toBeTruthy();
    const body = await res.json() as string[] | { permissions?: string[]; keys?: string[] };
    const list = Array.isArray(body) ? body : (body.permissions ?? body.keys ?? []);
    expect(list).toContain('portal.admin');

    await setOverrides(request, token, empId, []);
  });
});
