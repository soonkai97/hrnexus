import type { Page } from '@playwright/test';

export const ADMIN = { staffId: 'HRADMIN', password: 'Admin@1234' };

/**
 * Log into either the admin or staff app.
 * Both apps use identical login form IDs (#login-staff-id, #login-password).
 * The inputs start readonly (anti-autofill) — clicking fires mousedown which removes it.
 */
export async function loginAs(page: Page, staffId: string, password: string): Promise<void> {
  await page.goto('/auth/login');
  await page.locator('#login-staff-id').click();
  await page.locator('#login-staff-id').fill(staffId);
  await page.locator('#login-password').click();
  await page.locator('#login-password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
}
