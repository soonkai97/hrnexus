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
async function setAuthState(
  page: Page,
  staffId: string,
  password: string,
  portal: 'admin' | 'staff',
): Promise<void> {
  // Navigate first so fetch() runs in the correct origin context
  await page.goto('/auth/login');

  await page.evaluate(
    async ({ staffId, password, portal }) => {
      const res = await fetch('/api/auth/login', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ staffId, password, portal }),
      });
      if (!res.ok) {
        const text = await res.text().catch(() => '');
        throw new Error(`setAuthState: login failed ${res.status} — ${text}`);
      }
      const data = await res.json() as {
        accessToken: string;
        refreshToken: string;
        user: object;
      };
      localStorage.setItem('access_token',  data.accessToken);
      localStorage.setItem('refresh_token', data.refreshToken);
      localStorage.setItem('auth_user',     JSON.stringify(data.user));
    },
    { staffId, password, portal },
  );
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
