import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Tips Pooling
 *
 * WHY these tests matter:
 *  - Tips distribution is a financial action; if the form doesn't open or the
 *    distribution button is broken, managers can't split tips and staff miss pay.
 *  - The history view lets managers audit past distributions; a blank history
 *    (when records exist) breaks accountability.
 *  - The preview step prevents accidental distributions by showing allocations
 *    before confirmation.
 */
test.describe('Admin — Tips Pooling', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/tips');
    await page.waitForURL(/\/tips/);
  });

  test('shows the tips page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/tip/i, { timeout: 8_000 });
  });

  test('shows the Distribute Tips button', async ({ page }) => {
    await expect(
      page.getByRole('button', { name: /distribute/i })
    ).toBeVisible({ timeout: 8_000 });
  });

  test('opens the distribution form when button is clicked', async ({ page }) => {
    await page.getByRole('button', { name: /distribute/i }).click();
    await expect(page.locator('.form-card')).toBeVisible({ timeout: 5_000 });
  });

  test('shows distribution history or empty state after load', async ({ page }) => {
    // History section renders a table or an empty message
    await page.waitForSelector(
      '.history-card, .history-empty, .skeleton-table',
      { timeout: 10_000 }
    );
    const hasHistory = await page.locator('.history-card').count();
    const hasEmpty   = await page.locator('.history-empty').count();
    expect(hasHistory + hasEmpty).toBeGreaterThan(0);
  });
});
