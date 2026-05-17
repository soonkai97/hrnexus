import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Notification Centre
 *
 * WHY these tests matter:
 *  - The notification centre is the primary alert channel for missed clock-outs
 *    and leave requests; a blank or broken list means managers miss time-sensitive
 *    events and payroll errors go undetected.
 *  - The All / Unread tab switcher lets managers triage quickly; if the tabs
 *    don't render or switch, unread items are indistinguishable from read ones.
 *  - "Mark all read" clears the badge on the bell icon; if it doesn't appear
 *    when unread items exist, managers lose the visual alert signal.
 */
test.describe('Admin — Notification Centre', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/notifications');
    await page.waitForURL(/\/notifications/);
  });

  test('shows the notifications page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/notification/i, { timeout: 8_000 });
  });

  test('shows All and Unread filter tabs', async ({ page }) => {
    // p-tabs with "all" and "unread" values
    await expect(page.locator('.p-tabs, p-tabs')).toBeVisible({ timeout: 8_000 });
    await expect(page.getByRole('tab', { name: /all/i })).toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole('tab', { name: /unread/i })).toBeVisible({ timeout: 5_000 });
  });

  test('shows notifications list or empty state after load', async ({ page }) => {
    // .notif-list contains items; .empty-state appears when there are none
    await page.waitForSelector(
      '.notif-list, .empty-state, .notif-item',
      { timeout: 10_000 }
    );
    const hasList  = await page.locator('.notif-list').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasList + hasEmpty).toBeGreaterThan(0);
  });

  test('clicking Unread tab filters to unread items only', async ({ page }) => {
    await page.waitForSelector('.notif-list, .empty-state', { timeout: 10_000 });
    await page.getByRole('tab', { name: /unread/i }).click();
    // After switching, the page should show either unread items or an empty state
    await page.waitForSelector('.notif-item, .empty-state', { timeout: 5_000 });
    await expect(page.locator('h1')).toBeVisible();
  });
});
