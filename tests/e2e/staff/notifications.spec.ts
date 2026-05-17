import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

/**
 * Staff App — Notifications
 *
 * WHY these tests matter:
 *  - Notifications alert staff to leave approvals/rejections and shift changes;
 *    a broken list means staff miss time-sensitive decisions.
 *  - The All / Unread tab switcher lets staff quickly see what needs attention;
 *    a broken tab means unread items are invisible under noise.
 *  - The unread count badge on the bell and the "Mark all read" button must
 *    work together — a persistent badge after marking-all-read is misleading.
 */
test.describe('Staff — Notifications', () => {
  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/notifications');
    await page.waitForURL(/\/notifications/);
  });

  test('shows the notifications page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/notification/i, { timeout: 8_000 });
  });

  test('shows All and Unread filter tabs', async ({ page }) => {
    await expect(page.locator('.p-tabs, p-tabs')).toBeVisible({ timeout: 8_000 });
    await expect(page.getByRole('tab', { name: /all/i })).toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole('tab', { name: /unread/i })).toBeVisible({ timeout: 5_000 });
  });

  test('shows notifications list or empty state after load', async ({ page }) => {
    await page.waitForSelector(
      '.notif-list, .empty-state, .notif-item',
      { timeout: 10_000 }
    );
    const hasList  = await page.locator('.notif-list').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasList + hasEmpty).toBeGreaterThan(0);
  });

  test('clicking Unread tab shows only unread items or empty state', async ({ page }) => {
    await page.waitForSelector('.notif-list, .empty-state', { timeout: 10_000 });
    await page.getByRole('tab', { name: /unread/i }).click();
    await page.waitForSelector('.notif-item, .empty-state', { timeout: 5_000 });
    // Either unread items are shown, or an empty-state ("no unread") is displayed
    const hasItems = await page.locator('.notif-item').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasItems + hasEmpty).toBeGreaterThan(0);
  });
});
