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
 * Auth is now carried by an httpOnly cookie (no Bearer token in the body), so each identity gets
 * its OWN APIRequestContext: logging in on a context populates that context's cookie jar, and the
 * cookie is auto-sent on every subsequent request from it. This keeps the admin operator and the
 * employee subject cleanly separated without juggling tokens.
 *
 * Uses the seeded HRADMIN (SUPERADMIN, has every permission) as the operator and TESTSTAFF01
 * (EMPLOYEE, created by global-setup) as the subject. afterAll resets TESTSTAFF01's overrides so
 * the staff-portal specs still see a plain EMPLOYEE.
 */

const ADMIN_BASE = process.env['BASE_URL_ADMIN'] ?? 'http://localhost:8080';

const login = (ctx: APIRequestContext, staffId: string, password: string) =>
  ctx.post('/api/auth/login', { data: { staffId, password, portal: 'admin' } });

async function findEmployeeId(admin: APIRequestContext): Promise<string> {
  const res = await admin.get(`/api/staff?page=1&pageSize=20&includeHidden=true&search=${STAFF.staffId}`);
  expect(res.ok()).toBeTruthy();
  const body = await res.json() as { items?: { id: string; staffId: string }[]; data?: { id: string; staffId: string }[] };
  const items = body.items ?? body.data ?? (body as unknown as { id: string; staffId: string }[]);
  const found = items.find(s => s.staffId === STAFF.staffId);
  expect(found, `${STAFF.staffId} should exist (created by global-setup)`).toBeTruthy();
  return found!.id;
}

function setOverrides(
  admin: APIRequestContext, empId: string,
  overrides: { permissionKey: string; granted: boolean }[],
) {
  return admin.put(`/api/permissions/users/${empId}`, { data: { overrides } });
}

test.describe('Admin — permission framework', () => {
  let empId = '';

  test.beforeAll(async ({ request }) => {
    expect((await login(request, ADMIN.staffId, ADMIN.password)).ok(), 'HRADMIN admin login should succeed').toBeTruthy();
    empId = await findEmployeeId(request);
    await setOverrides(request, empId, []);   // clean slate
  });

  test.afterAll(async ({ request }) => {
    // Reset so other specs (and live use) see TESTSTAFF01 as a plain EMPLOYEE again.
    await login(request, ADMIN.staffId, ADMIN.password);
    const id = await findEmployeeId(request);
    await setOverrides(request, id, []);
  });

  test('admin-portal login gate: SUPERADMIN allowed, EMPLOYEE rejected', async ({ playwright }) => {
    const adminCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(adminCtx, ADMIN.staffId, ADMIN.password)).ok()).toBeTruthy();
    await adminCtx.dispose();

    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok(),
      'a plain EMPLOYEE must not be able to sign into the admin portal').toBeFalsy();
    await empCtx.dispose();
  });

  test('a per-user GRANT override unlocks admin login immediately; revoking re-locks it', async ({ request, playwright }) => {
    await login(request, ADMIN.staffId, ADMIN.password);

    // Grant portal.admin to the employee…
    expect((await setOverrides(request, empId, [{ permissionKey: 'portal.admin', granted: true }])).ok()).toBeTruthy();
    const grantCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(grantCtx, STAFF.staffId, STAFF.password)).ok(),
      'granting portal.admin should let the employee in on the very next login').toBeTruthy();
    await grantCtx.dispose();

    // …then clear it again.
    expect((await setOverrides(request, empId, [])).ok()).toBeTruthy();
    const clearCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(clearCtx, STAFF.staffId, STAFF.password)).ok(),
      'clearing the grant should lock the employee back out immediately').toBeFalsy();
    await clearCtx.dispose();
  });

  test('a policy-protected endpoint is 403 without the permission, 200 with it', async ({ request, playwright }) => {
    await login(request, ADMIN.staffId, ADMIN.password);

    // Grant ONLY portal.admin so the employee can authenticate to admin, but NOT roles.manage.
    await setOverrides(request, empId, [{ permissionKey: 'portal.admin', granted: true }]);
    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok()).toBeTruthy();

    // GET /api/permissions (the catalog) requires roles.manage.
    const forbidden = await empCtx.get('/api/permissions');
    expect(forbidden.status(), 'employee without roles.manage should get 403').toBe(403);
    await empCtx.dispose();

    const allowed = await request.get('/api/permissions');
    expect(allowed.ok(), 'HRADMIN has roles.manage → 200').toBeTruthy();

    await setOverrides(request, empId, []);
  });

  test('effective-permissions endpoint reflects the granted override', async ({ request, playwright }) => {
    await login(request, ADMIN.staffId, ADMIN.password);

    await setOverrides(request, empId, [{ permissionKey: 'portal.admin', granted: true }]);
    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok()).toBeTruthy();

    const res = await empCtx.get('/api/auth/me/permissions');
    expect(res.ok()).toBeTruthy();
    const body = await res.json() as string[] | { permissions?: string[]; keys?: string[] };
    const list = Array.isArray(body) ? body : (body.permissions ?? body.keys ?? []);
    expect(list).toContain('portal.admin');
    await empCtx.dispose();

    await setOverrides(request, empId, []);
  });
});
