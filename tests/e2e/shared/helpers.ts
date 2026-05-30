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
 * Authenticate by calling the API directly from the browser context and
 * writing the tokens to localStorage — exactly what AuthService.storeSession()
 * does.  Bypasses the reactive-form / readonly-input interaction that can be
 * flaky with Playwright's Mobile Chrome emulation.
 */
interface AuthSession { accessToken: string; refreshToken: string; user: object; }

// Cache one session per (staffId, portal) for the whole worker run. The suite authenticates on
// every test; without caching that's ~90 logins from a single source IP, which trips the per-IP
// auth rate limiter (a real prod safeguard) and floods the run with 429s. Logging in ONCE per
// account and replaying the tokens into localStorage avoids hammering the endpoint. JWTs last an
// hour, far longer than a test run.
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
    // Log in once (Node-side request), with a short back-off if the limiter is warm.
    let lastStatus = 0, lastBody = '';
    for (let attempt = 0; attempt < 4 && !session; attempt++) {
      const res = await page.request.post('/api/auth/login', {
        data: { staffId, password, portal },
      });
      lastStatus = res.status();
      if (res.ok()) { session = await res.json() as AuthSession; break; }
      lastBody = await res.text().catch(() => '');
      if (lastStatus === 429) await page.waitForTimeout(2000); else break;
    }
    if (!session) throw new Error(`setAuthState: login failed ${lastStatus} — ${lastBody}`);
    sessionCache.set(key, session);
  }

  // Navigate first so localStorage is written for the correct origin.
  await page.goto('/auth/login');
  await page.evaluate((s: AuthSession) => {
    localStorage.setItem('access_token',  s.accessToken);
    localStorage.setItem('refresh_token', s.refreshToken);
    localStorage.setItem('auth_user',     JSON.stringify(s.user));
  }, session);
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
