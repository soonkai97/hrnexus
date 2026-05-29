import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

test.describe('Staff app — log shift page', () => {

  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/log-shift');
    await page.waitForURL(/\/log-shift/);
  });

  test('log-shift page renders without error', async ({ page }) => {
    // The page must render something visible — not a blank screen or error
    await expect(page.locator('body')).not.toBeEmpty();
    // Should show mode toggle tabs (Live / Manual) inside the Log tab
    // PrimeNG p-tab renders with role="tab", not role="button"
    await expect(page.getByRole('tab', { name: /manual/i })).toBeVisible({ timeout: 5_000 });
  });

  test('switching to Manual tab shows the manual entry form', async ({ page }) => {
    await page.getByRole('tab', { name: /manual/i }).click();
    // The manual form has a date field plus several time pickers — assert the first
    await expect(page.locator('input[type="date"], p-datepicker').first()).toBeVisible({ timeout: 5_000 });
  });

  test('history tab shows completed shift records or an empty state', async ({ page }) => {
    // PrimeNG p-tab renders with role="tab", not role="button"
    await page.getByRole('tab', { name: /history/i }).click();
    // Either history cards (.history-card) or an empty-state message — never a blank crash.
    // .first() avoids strict-mode violation: the empty state contains both
    // .empty-state and a nested .empty-icon, which both match [class*="empty"].
    await expect(
      page.locator('.history-card, .empty-state').first()
    ).toBeVisible({ timeout: 6_000 });
  });

});
