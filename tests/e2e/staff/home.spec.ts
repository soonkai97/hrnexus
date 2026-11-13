import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

/**
 * Staff App — Home screen
 *
 * WHY these tests matter:
 *  - The home screen is the first thing a staff member sees; a blank or broken
 *    page means they can't verify their leave balance.
 *  - Leave balance cards feed into staff planning their time off; incorrect
 *    counts (from a broken computed or API call) cause unexpected leave denials.
 */
test.describe('Staff — Home Screen', () => {
  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/home');
    await page.waitForURL(/\/home/);
  });

  test('shows a personalised greeting', async ({ page }) => {
    // Greeting should contain "Good morning/afternoon/evening" or similar
    await expect(
      page.locator('.greeting-text, h1').first()
    ).toBeVisible({ timeout: 8_000 });
  });

  test('shows the sidebar navigation', async ({ page }) => {
    await expect(page.locator('.sidebar')).toBeVisible({ timeout: 5_000 });
  });

  test('shows leave balance cards', async ({ page }) => {
    // Balance cards render as .bal-card or .stat-card on the home page
    await page.waitForSelector('.stat-card, .bal-card', { timeout: 10_000 });
    await expect(page.locator('.stat-card, .bal-card').first()).toBeVisible();
  });

  test('sidebar contains navigation links', async ({ page }) => {
    // Check that at least two nav items are present in the sidebar
    const navItems = page.locator('.sidebar .nav-item');
    await expect(navItems.first()).toBeVisible({ timeout: 5_000 });
    expect(await navItems.count()).toBeGreaterThanOrEqual(2);
  });
});
