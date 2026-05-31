import type { FullConfig } from '@playwright/test';
import { execSync } from 'child_process';

/**
 * Global setup — runs once before all Playwright tests.
 *
 * Creates the TESTSTAFF01 employee account that staff-app E2E tests need.
 * HRADMIN (admin role) is blocked from the staff portal, so the tests need
 * a dedicated EMPLOYEE-role account.
 *
 * Safe to run repeatedly: a 409 Conflict response means the account already
 * exists and is silently skipped.
 */
const ADMIN_BASE = process.env['BASE_URL_ADMIN'] ?? 'http://localhost:8080';

export const STAFF_CREDS = {
  staffId:  'TESTSTAFF01',
  password: 'Test@1234',
};

export default async function globalSetup(_config: FullConfig): Promise<void> {
  // ── Step 0: Enable all feature flags in the test DB ──────────────────────────
  // Pass the SQL via stdin (not -c "...") so the double-quoted Postgres
  // identifiers survive cross-platform shell quoting — cmd.exe on Windows does
  // not handle \"escaped quotes\" the way bash does, which silently breaks -c.
  try {
    execSync(
      `docker compose exec -T postgres psql -U ncuser -d hr_nexus`,
      {
        cwd:   process.cwd(),
        input: 'UPDATE "FeatureFlags" SET "IsEnabled" = true;',
        stdio: ['pipe', 'pipe', 'pipe'],
      }
    );
    console.log('[global-setup] All feature flags enabled.');
  } catch (err) {
    console.warn('[global-setup] Warning: Could not enable feature flags:', err);
  }

  // ── Step 1: Authenticate as HRADMIN ─────────────────────────────────────────
  // Tokens come back as httpOnly Set-Cookie headers (not in the body), so we capture and replay
  // them as a Cookie header on the follow-up request.
  let cookieHeader: string;
  try {
    const loginRes = await fetch(`${ADMIN_BASE}/api/auth/login`, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ staffId: 'HRADMIN', password: 'Admin@1234', portal: 'admin' }),
    });

    if (!loginRes.ok) {
      console.warn(`[global-setup] HRADMIN login failed (HTTP ${loginRes.status}). Staff E2E tests will fail.`);
      return;
    }

    const setCookies = loginRes.headers.getSetCookie?.() ?? [];
    cookieHeader = setCookies
      .map(c => c.split(';')[0])   // "name=value"
      .filter(c => c.startsWith('hrnexus_at=') || c.startsWith('hrnexus_rt='))
      .join('; ');
    if (!cookieHeader) {
      console.warn('[global-setup] No auth cookies in login response. Staff E2E tests will fail.');
      return;
    }
  } catch (err) {
    console.warn(`[global-setup] Could not reach API at ${ADMIN_BASE}. Is Docker running? Staff E2E tests will fail.\n${err}`);
    return;
  }

  // ── Step 2: Create the test staff account ────────────────────────────────────
  try {
    const registerRes = await fetch(`${ADMIN_BASE}/api/staff`, {
      method:  'POST',
      headers: {
        'Content-Type': 'application/json',
        'Cookie':       cookieHeader,
      },
      body: JSON.stringify({
        staffId:   STAFF_CREDS.staffId,
        firstName: 'Test',
        lastName:  'Staff',
        password:  STAFF_CREDS.password,
        roles:     ['EMPLOYEE'],
      }),
    });

    if (registerRes.status === 409) {
      // Already exists — nothing to do
      return;
    }

    if (!registerRes.ok) {
      const body = await registerRes.text();
      console.warn(`[global-setup] Failed to create ${STAFF_CREDS.staffId} (HTTP ${registerRes.status}): ${body}. Staff E2E tests will fail.`);
    }
  } catch (err) {
    console.warn(`[global-setup] Error registering test staff account: ${err}. Staff E2E tests will fail.`);
  }
}
