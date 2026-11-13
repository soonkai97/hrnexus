import { test, expect } from '@playwright/test';
import { loginAs, loginStaff, ADMIN } from '../shared/helpers';

/**
 * Staff app auth flow E2E.
 * The staff app is a mobile PWA — same login form, different landing page (/home).
 */
test.describe('Staff app — authentication', () => {

  test('successful login lands on /home', async ({ page }) => {
    await loginStaff(page);
    await expect(page).toHaveURL(/\/home/);
  });

  test('wrong password shows an error message', async ({ page }) => {
    await loginAs(page, ADMIN.staffId, 'badpassword');
    await expect(page.locator('p-message[severity="error"], .p-message-error')).toBeVisible({ timeout: 5_000 });
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('accessing /my-leave while logged out redirects to /auth/login', async ({ page }) => {
    await page.goto('/my-leave');
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

  test('accessing /home while logged out redirects to /auth/login', async ({ page }) => {
    await page.goto('/home');
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

  test('logout from profile page navigates back to login', async ({ page }) => {
    await loginStaff(page);
    await page.goto('/profile');
    await page.getByRole('button', { name: /sign out/i }).click();
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

});
