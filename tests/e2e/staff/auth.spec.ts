import { test, expect } from '@playwright/test';
import { loginAs, loginStaff, ADMIN } from '../shared/helpers';

/**
 * Employee auth flow E2E: a plain employee signs in to the same app and sees only the self-service pages.
 */
test.describe('Employee — authentication', () => {

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

  test('an employee is sent back to /home from every management page', async ({ page }) => {
    await loginStaff(page);
    for (const path of ['/dashboard', '/staff', '/leave', '/roles', '/payroll-locks', '/cash-advances']) {
      await page.goto(path);
      await expect(page, `${path} must bounce an employee`).toHaveURL(/\/home/, { timeout: 10_000 });
    }
  });

  test('an employee sees only the self-service menu', async ({ page }) => {
    await loginStaff(page);
    // toHaveText retries until the menu has rendered and the permissions have loaded.
    await expect(page.locator('.sidebar .nav-item .nav-label')).toHaveText(['Home', 'My Leave', 'Profile']);
  });

  test('logout from profile page navigates back to login', async ({ page }) => {
    await loginStaff(page);
    await page.goto('/profile');
    await page.getByRole('button', { name: /sign out/i }).click();
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 5_000 });
  });

});
