import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Statutory rates page (Phase 8b).
 *
 * WHY: the rate table drives every payslip, so the administrator must be able to open it and see
 * every row. These tests are read-only on purpose: verifying a rate is permanent and cannot be
 * undone, and other runs may already have verified rows, so nothing here assumes the data is
 * unverified and nothing verifies a rate through the UI.
 */
test.describe('Admin: Statutory Rates', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/payroll-rates');
    await page.waitForURL(/\/payroll-rates/);
  });

  test('shows the heading and every rate as a table, and as cards when chosen', async ({
    page,
  }) => {
    await expect(page.getByRole('heading', { name: 'Statutory Rates' })).toBeVisible({
      timeout: 8_000,
    });
    // Six schemes for two citizenship classes.
    await expect
      .poll(async () => page.locator('p-table tbody tr').count(), { timeout: 8_000 })
      .toBeGreaterThanOrEqual(12);

    await page.getByRole('button', { name: 'Card view' }).click();
    await expect
      .poll(async () => page.locator('.nc-card').count(), { timeout: 8_000 })
      .toBeGreaterThanOrEqual(12);
  });

  test('shows the placeholder banner exactly while a row is unverified', async ({ page }) => {
    await expect(page.locator('p-table tbody tr').first()).toBeVisible({ timeout: 8_000 });
    const unverified = await page.getByText('Unverified', { exact: true }).count();
    const banner = page.getByText(/placeholders until your accountant verifies them/i);
    if (unverified > 0) {
      await expect(banner).toBeVisible();
    } else {
      await expect(banner).toHaveCount(0);
    }
  });
});
