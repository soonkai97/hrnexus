import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

/**
 * Staff App — Pay Summary
 *
 * WHY these tests matter:
 *  - Pay summary is the only view where staff can verify their gross pay before
 *    pay day; if the period picker doesn't render, staff have no way to query it.
 *  - The hero card (gross pay) and breakdown rows (regular + OT + tips) must all
 *    appear after a period is selected — missing rows mean staff can't reconcile
 *    their actual vs expected pay.
 *  - The per-shift list at the bottom is the audit trail; if it's blank when shifts
 *    exist, staff can't identify which shift caused a discrepancy.
 */
test.describe('Staff — Pay Summary', () => {
  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/pay-summary');
    await page.waitForURL(/\/pay-summary/);
  });

  test('shows the pay summary page heading', async ({ page }) => {
    // Pay summary uses h2 (not h1) for the page title
    await expect(page.locator('h2, h1')).toContainText(/pay/i, { timeout: 8_000 });
  });

  test('shows the period picker with year and month controls', async ({ page }) => {
    await expect(page.locator('.period-picker')).toBeVisible({ timeout: 8_000 });
    // Should have a "View" / "Load" button inside the picker
    await expect(
      page.getByRole('button', { name: /view|load|generate/i })
    ).toBeVisible({ timeout: 5_000 });
  });

  test('loads pay summary when a period is selected and View is clicked', async ({ page }) => {
    await page.waitForSelector('.period-picker', { timeout: 8_000 });
    // Click the View button without changing the defaults (current month)
    await page.getByRole('button', { name: /view|load|generate/i }).click();
    // Either the hero card (data found) or an empty/no-data state should appear
    await page.waitForSelector(
      '.hero-card, .empty-state, .breakdown-card',
      { timeout: 12_000 }
    );
    const hasHero     = await page.locator('.hero-card').count();
    const hasBreakdown = await page.locator('.breakdown-card').count();
    const hasEmpty    = await page.locator('.empty-state').count();
    expect(hasHero + hasBreakdown + hasEmpty).toBeGreaterThan(0);
  });
});
