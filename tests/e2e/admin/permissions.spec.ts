import { test, expect, type APIRequestContext } from '@playwright/test';
import { ADMIN, STAFF } from '../shared/helpers';

/**
 * Permission-framework E2E — exercised through the real HTTP API (the backend is the actual
 * enforcement boundary; the FE guards are defence-in-depth on top). One web app serves every
 * role, so access is decided by permissions, not by which app you sign in to. Proves:
 *  - any active account can sign in (SUPERADMIN and a plain EMPLOYEE alike),
 *  - management endpoints return 403 without the permission (roles.manage, staff.view) and 200 with it,
 *  - per-user GRANT/REVOKE overrides take effect IMMEDIATELY (DB-resolved, not baked into the JWT),
 *  - the effective-permissions endpoint reflects the override.
 *
 * Auth is now carried by an httpOnly cookie (no Bearer token in the body), so each identity gets
 * its OWN APIRequestContext: logging in on a context populates that context's cookie jar, and the
 * cookie is auto-sent on every subsequent request from it. This keeps the admin operator and the
 * employee subject cleanly separated without juggling tokens.
 *
 * Uses the seeded HRADMIN (SUPERADMIN, has every permission) as the operator and TESTSTAFF01
 * (EMPLOYEE, created by global-setup, no permissions by default) as the subject. afterAll resets
 * TESTSTAFF01's overrides so the staff specs still see a plain EMPLOYEE.
 */

const ADMIN_BASE = process.env['BASE_URL'] ?? 'http://localhost:8080';

const login = (ctx: APIRequestContext, staffId: string, password: string) =>
  ctx.post('/api/auth/login', { data: { staffId, password } });

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
    expect((await login(request, ADMIN.staffId, ADMIN.password)).ok(), 'HRADMIN login should succeed').toBeTruthy();
    empId = await findEmployeeId(request);
    await setOverrides(request, empId, []);   // clean slate
  });

  test.afterAll(async ({ request }) => {
    // Reset so other specs (and live use) see TESTSTAFF01 as a plain EMPLOYEE again.
    await login(request, ADMIN.staffId, ADMIN.password);
    const id = await findEmployeeId(request);
    await setOverrides(request, id, []);
  });

  test('any active account can sign in: SUPERADMIN and a plain EMPLOYEE', async ({ playwright }) => {
    const adminCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(adminCtx, ADMIN.staffId, ADMIN.password)).ok()).toBeTruthy();
    await adminCtx.dispose();

    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok(),
      'a plain EMPLOYEE must be able to sign in (one app for every role)').toBeTruthy();
    await empCtx.dispose();
  });

  test('without roles.manage the roles/permissions endpoints return 403, with it 200', async ({ request, playwright }) => {
    await login(request, ADMIN.staffId, ADMIN.password);

    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok()).toBeTruthy();

    // GET /api/permissions (the catalog) requires roles.manage.
    const forbidden = await empCtx.get('/api/permissions');
    expect(forbidden.status(), 'employee without roles.manage should get 403').toBe(403);
    await empCtx.dispose();

    const allowed = await request.get('/api/permissions');
    expect(allowed.ok(), 'HRADMIN has roles.manage → 200').toBeTruthy();
  });

  test('a per-user GRANT of staff.view changes what the API allows on the next request; revoking re-locks it', async ({ request, playwright }) => {
    await login(request, ADMIN.staffId, ADMIN.password);

    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok()).toBeTruthy();
    const staffList = '/api/staff?page=1&pageSize=5';

    // Starts locked out of the staff list…
    expect((await empCtx.get(staffList)).status(), 'plain EMPLOYEE cannot list staff').toBe(403);

    // …grant staff.view and the SAME session (no re-login) is allowed on the very next request…
    expect((await setOverrides(request, empId, [{ permissionKey: 'staff.view', granted: true }])).ok()).toBeTruthy();
    expect((await empCtx.get(staffList)).ok(),
      'granting staff.view should unlock the staff list immediately').toBeTruthy();

    // …then clear it again and the door shuts immediately.
    expect((await setOverrides(request, empId, [])).ok()).toBeTruthy();
    expect((await empCtx.get(staffList)).status(),
      'clearing the grant should lock the employee out again immediately').toBe(403);
    await empCtx.dispose();
  });

  test('effective-permissions endpoint reflects the granted override', async ({ request, playwright }) => {
    await login(request, ADMIN.staffId, ADMIN.password);

    await setOverrides(request, empId, [{ permissionKey: 'staff.view', granted: true }]);
    const empCtx = await playwright.request.newContext({ baseURL: ADMIN_BASE });
    expect((await login(empCtx, STAFF.staffId, STAFF.password)).ok()).toBeTruthy();

    const res = await empCtx.get('/api/auth/me/permissions');
    expect(res.ok()).toBeTruthy();
    const body = await res.json() as string[] | { permissions?: string[]; keys?: string[] };
    const list = Array.isArray(body) ? body : (body.permissions ?? body.keys ?? []);
    expect(list).toContain('staff.view');
    await empCtx.dispose();

    await setOverrides(request, empId, []);
  });
});
