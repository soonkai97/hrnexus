import { request } from '@playwright/test';
import { ensureRole } from './api';

/**
 * Global setup — runs once before all Playwright tests.
 *
 * Creates the TESTSTAFF01 employee account that the employee E2E scenarios need.
 * The staff specs assert a plain employee's view, so they need a dedicated
 * EMPLOYEE-role account rather than the HRADMIN administrator.
 *
 * Safe to run repeatedly: a 409 Conflict response means the account already
 * exists and is silently skipped.
 */
const ADMIN_BASE = process.env['BASE_URL'] ?? 'http://localhost:8080';

export const STAFF_CREDS = {
  staffId:  'TESTSTAFF01',
  password: 'Test@1234',
};

export default async function globalSetup(): Promise<void> {
  // Tokens come back as httpOnly Set-Cookie headers; the request context stores and replays them.
  const api = await request.newContext({ baseURL: ADMIN_BASE });

  try {
    // ── Step 1: Authenticate as HRADMIN ───────────────────────────────────────
    let loginStatus: number;
    try {
      const login = await api.post('/api/auth/login', {
        data: { staffId: 'HRADMIN', password: 'Admin@1234' },
      });
      loginStatus = login.status();
    } catch (err) {
      console.warn(`[global-setup] Could not reach API at ${ADMIN_BASE}. Is Docker running? Staff E2E tests will fail.\n${err}`);
      return;
    }
    if (loginStatus !== 200) {
      console.warn(`[global-setup] HRADMIN login failed (HTTP ${loginStatus}). Staff E2E tests will fail.`);
      return;
    }

    // ── Step 2: Create the test staff account (roles are identified by GUID) ──
    try {
      const employeeRoleId = await ensureRole(api, 'EMPLOYEE');
      const register = await api.post('/api/staff', {
        data: {
          staffId:   STAFF_CREDS.staffId,
          firstName: 'Test',
          lastName:  'Staff',
          password:  STAFF_CREDS.password,
          roles:     [employeeRoleId],
        },
      });

      // 409 = the account already exists from an earlier run — nothing to do.
      if (!register.ok() && register.status() !== 409) {
        console.warn(`[global-setup] Failed to create ${STAFF_CREDS.staffId} (HTTP ${register.status()}): ${await register.text()}. Staff E2E tests will fail.`);
      }
    } catch (err) {
      console.warn(`[global-setup] Error registering test staff account: ${err}. Staff E2E tests will fail.`);
    }
  } finally {
    await api.dispose();
  }
}
