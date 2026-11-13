import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Dashboard
 *
 * WHY these tests matter:
 *  - Dashboard is the first page managers see after login; a broken redirect
 *    or blank KPI panel means managers can't gauge the venue at a glance.
 *  - The SignalR "Live" badge signals real-time data is flowing; if it's missing
 *    the manager doesn't know whether the counts are current.
 */
test.describe('Admin — Dashboard', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
  });

  test('redirects to /dashboard after login', async ({ page }) => {
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('shows the total staff KPI card', async ({ page }) => {
    // Wait for the loading skeleton to clear, then check the KPI card.
    await page.waitForSelector('.stat-card', { timeout: 10_000 });
    await expect(page.locator('.stat-card .stat-value').first()).toBeVisible();
  });

  test('shows the live/connecting status badge', async ({ page }) => {
    await page.waitForSelector('.stat-card', { timeout: 10_000 });
    await expect(page.locator('p-tag').first()).toBeVisible({ timeout: 10_000 });
  });
});
