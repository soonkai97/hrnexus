import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Dashboard
 *
 * WHY these tests matter:
 *  - Dashboard is the first page managers see after login; a broken redirect
 *    or blank KPI panel means managers can't gauge venue status at a glance.
 *  - The SignalR "Live" badge signals real-time data is flowing; if it's missing
 *    the manager doesn't know whether the counts are current.
 *  - The on-shift table shows who's working right now — critical during a shift.
 */
test.describe('Admin — Dashboard', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
  });

  test('redirects to /dashboard after login', async ({ page }) => {
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('shows four KPI stat cards', async ({ page }) => {
    // Wait for the loading skeleton to clear then check KPI cards.
    // There are always 4 KPI cards; an optional 5th warn card appears when openShiftsCount > 0.
    await page.waitForSelector('.stat-card', { timeout: 10_000 });
    const count = await page.locator('.stat-card').count();
    expect(count).toBeGreaterThanOrEqual(4);
  });

  test('shows the on-shift staff section', async ({ page }) => {
    await page.waitForSelector('.stat-card', { timeout: 10_000 });
    // Either the on-shift table or an empty state message should be present
    const onShiftSection = page.locator('text=/on shift|currently working|no staff/i').first();
    await expect(onShiftSection).toBeVisible({ timeout: 10_000 });
  });

  test('shows the live activity feed section', async ({ page }) => {
    await page.waitForSelector('.stat-card', { timeout: 10_000 });
    const feedSection = page.locator('text=/activity|recent|event/i').first();
    await expect(feedSection).toBeVisible({ timeout: 10_000 });
  });
});
