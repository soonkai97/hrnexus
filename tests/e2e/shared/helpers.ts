import type { Page } from '@playwright/test';
import { STAFF_CREDS } from './global-setup';

export const ADMIN = { staffId: 'HRADMIN', password: 'Admin@1234' };
/** Employee-role account created by global-setup — the only account that can log into the staff portal. */
export const STAFF = STAFF_CREDS;

/**
 * Low-level form-based login — kept for tests that explicitly test the login
 * UI (wrong password, field validation, etc.).
 *
 * Both apps use identical form IDs. Inputs start readonly (anti-autofill);
 * Playwright's .click() fires mousedown → Angular's removeReadonly() is called.
 */
export async function loginAs(page: Page, staffId: string, password: string): Promise<void> {
  await page.goto('/auth/login');
  await page.locator('#login-staff-id').click();
  await page.locator('#login-staff-id').fill(staffId);
  await page.locator('#login-password').click();
  await page.locator('#login-password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
}

/**
 * Authenticate by calling the API directly from the browser context. The tokens come back as
 * httpOnly cookies (which land in the page's browser context automatically); we capture them and
 * replay them into each fresh context, plus seed the non-sensitive `auth_user` the SPA reads to
 * know who's signed in. Bypasses the reactive-form / readonly-input interaction that can be flaky
 * with Playwright's Mobile Chrome emulation.
 */
interface AuthSession { user: object; cookies: import('@playwright/test').Cookie[]; }

// Cache one session per (staffId, portal) for the whole worker run. The suite authenticates on
// every test; without caching that's ~90 logins from a single source IP, which trips the per-IP
// auth rate limiter (a real prod safeguard) and floods the run with 429s. Logging in ONCE per
// account and replaying the auth COOKIES into each context avoids hammering the endpoint. The
// access-token JWT lasts far longer than a test run.
const sessionCache = new Map<string, AuthSession>();

async function setAuthState(
  page: Page,
  staffId: string,
  password: string,
  portal: 'admin' | 'staff',
): Promise<void> {
  const key = `${staffId}:${portal}`;
  let session = sessionCache.get(key);

  if (!session) {
    // Log in once (the Set-Cookie lands in this page's context), with a short back-off if warm.
    let lastStatus = 0, lastBody = '', user: object | null = null;
    for (let attempt = 0; attempt < 4 && !user; attempt++) {
      const res = await page.request.post('/api/auth/login', {
        data: { staffId, password, portal },
      });
      lastStatus = res.status();
      if (res.ok()) { user = ((await res.json()) as { user: object }).user; break; }
      lastBody = await res.text().catch(() => '');
      if (lastStatus === 429) await page.waitForTimeout(2000); else break;
    }
    if (!user) throw new Error(`setAuthState: login failed ${lastStatus} — ${lastBody}`);

    const cookies = (await page.context().cookies())
      .filter(c => c.name === 'hrnexus_at' || c.name === 'hrnexus_rt');
    session = { user, cookies };
    sessionCache.set(key, session);
  }

  // Replay the auth cookies into this (possibly fresh) context, then seed the user profile.
  await page.context().addCookies(session.cookies);
  await page.goto('/auth/login');
  await page.evaluate((u: object) => {
    localStorage.setItem('auth_user', JSON.stringify(u));
  }, session.user);
}

/**
 * Login to the admin app (project baseURL = http://localhost:8080).
 * Uses the direct-API approach so the reactive form cannot interfere.
 */
export async function loginAdmin(page: Page): Promise<void> {
  await setAuthState(page, ADMIN.staffId, ADMIN.password, 'admin');
  await page.goto('/dashboard');
  await page.waitForURL(/\/dashboard/, { timeout: 10_000 });
}

/**
 * Login to the staff app (project baseURL = http://localhost:8081).
 * Uses TESTSTAFF01 (EMPLOYEE role) — HRADMIN is blocked from the staff portal.
 * The account is created by global-setup before any tests run.
 */
export async function loginStaff(page: Page): Promise<void> {
  await setAuthState(page, STAFF.staffId, STAFF.password, 'staff');
  await page.goto('/home');
  await page.waitForURL(/\/home/, { timeout: 10_000 });
}
