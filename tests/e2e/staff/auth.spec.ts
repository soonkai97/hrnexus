import { test, expect } from '@playwright/test';

/**
 * Staff App — Authentication flow
 * Tests login and route protection.
 *
 * Form inputs are identified via for/id label linkage (accessibility fix applied).
 */
test.describe('Staff App — Authentication', () => {
  test('redirects unauthenticated user to login', async ({ page }) => {
    await page.goto('/home');
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('shows login form with Staff ID and Password inputs', async ({ page }) => {
    await page.goto('/auth/login');
    await expect(page.getByLabel('Staff ID', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
  });

  test('shows sign-in button', async ({ page }) => {
    await page.goto('/auth/login');
    await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
  });
});
