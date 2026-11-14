import { test, expect } from '@playwright/test';
import { loginAs, loginAdmin, ADMIN } from '../shared/helpers';

/**
 * Administrator auth flow E2E.
 * These tests guard the login → home path and the authGuard redirect.
 * A regression here means no one can sign in to the app.
 */
test.describe('Administrator — authentication', () => {

  test('successful login lands on /home', async ({ page }) => {
    await loginAs(page, ADMIN.staffId, ADMIN.password);
    await expect(page).toHaveURL(/\/home/, { timeout: 10_000 });
    await expect(page.locator('.sidebar')).toBeVisible();
  });

  test('wrong password shows an inline error message', async ({ page }) => {
    await loginAs(page, ADMIN.staffId, 'wrongpassword');
    // p-message severity="error" renders a visible alert
    await expect(page.locator('p-message[severity="error"], .p-message-error')).toBeVisible({ timeout: 5_000 });
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('accessing /dashboard while logged out redirects to /auth/login', async ({ page }) => {
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

  test('accessing /staff while logged out redirects to /auth/login', async ({ page }) => {
    await page.goto('/staff');
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

  test('logout navigates back to login page', async ({ page }) => {
    await loginAdmin(page);
    // The Sign out button lives on the profile page
    await page.goto('/profile');
    await page.getByRole('button', { name: /sign out/i }).click();
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

});
