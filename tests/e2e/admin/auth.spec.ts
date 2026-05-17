import { test, expect } from '@playwright/test';

/**
 * Admin App — Authentication flow
 * Tests login, logout, and route guard protection.
 *
 * Form inputs are identified via for/id label linkage (accessibility fix applied).
 * The translated label text (English) is used for getByLabel() selectors.
 */
test.describe('Admin — Authentication', () => {
  test('redirects unauthenticated user to login', async ({ page }) => {
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('shows login form with Staff ID and Password fields', async ({ page }) => {
    await page.goto('/auth/login');
    await expect(page.getByLabel('Staff ID', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
  });

  test('shows sign-in button', async ({ page }) => {
    await page.goto('/auth/login');
    await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
  });

  test('shows error on invalid credentials', async ({ page }) => {
    await page.goto('/auth/login');

    // Click inputs first to remove the readonly attribute (anti-autofill technique)
    const staffInput = page.getByLabel('Staff ID');
    await staffInput.click();
    await staffInput.fill('INVALID');

    const pwdInput = page.getByLabel('Password', { exact: true });
    await pwdInput.click();
    await pwdInput.fill('wrongpassword');

    await page.getByRole('button', { name: /sign in/i }).click();

    // PrimeNG message component renders with .p-message class
    await expect(page.locator('.p-message')).toBeVisible({ timeout: 8000 });
  });
});
