import { test, expect } from '@playwright/test';
import { loginAs, ADMIN } from '../shared/helpers';

test.describe('Staff app — log shift page', () => {

  test.beforeEach(async ({ page }) => {
    await loginAs(page, ADMIN.staffId, ADMIN.password);
    await expect(page).toHaveURL(/\/home/, { timeout: 10_000 });
    await page.goto('/log-shift');
  });

  test('log-shift page renders without error', async ({ page }) => {
    // The page must render something visible — not a blank screen or error
    await expect(page.locator('body')).not.toBeEmpty();
    // Should show mode toggle buttons (Live / Manual)
    await expect(page.getByRole('button', { name: /manual/i })).toBeVisible({ timeout: 5_000 });
  });

  test('switching to Manual tab shows the manual entry form', async ({ page }) => {
    await page.getByRole('button', { name: /manual/i }).click();
    // The manual form has a date field and time pickers
    await expect(page.locator('input[type="date"], p-datepicker')).toBeVisible({ timeout: 5_000 });
  });

  test('history tab shows completed shift records or an empty state', async ({ page }) => {
    await page.getByRole('button', { name: /history/i }).click();
    // Either shift cards or an empty-state message must appear — never a blank crash
    await expect(
      page.locator('.shift-card, .empty-state, [class*="empty"]')
    ).toBeVisible({ timeout: 6_000 });
  });

});
