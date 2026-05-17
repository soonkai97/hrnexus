import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

/**
 * Staff App — Log Shift
 *
 * WHY these tests matter:
 *  - Clock-in/out is the core daily action for staff; if the form doesn't render
 *    or the Live/Manual mode tabs are broken, staff can't log their hours.
 *  - The History tab is the primary record of worked shifts; if it doesn't load
 *    or shows a blank screen, staff can't verify their own hours before pay day.
 *  - Separate Live and Manual modes serve different use cases: Live for real-time
 *    clock-in; Manual for retroactive entries (e.g. forgot to clock in).
 */
test.describe('Staff — Log Shift', () => {
  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/log-shift');
    await page.waitForURL(/\/log-shift/);
  });

  test('shows the log shift page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/shift|log/i, { timeout: 8_000 });
  });

  test('shows Log and History view tabs', async ({ page }) => {
    // p-tabs with "log" and "history" values
    await expect(page.locator('.p-tabs, p-tabs')).toBeVisible({ timeout: 8_000 });
    await expect(page.getByRole('tab', { name: /log/i }).first()).toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole('tab', { name: /history/i }).first()).toBeVisible({ timeout: 5_000 });
  });

  test('shows shift log form or active shift card in the Log tab', async ({ page }) => {
    // Either an active-card (already clocked in) or the form-card (ready to start)
    await page.waitForSelector(
      '.active-card, .form-card',
      { timeout: 10_000 }
    );
    const hasActive = await page.locator('.active-card').count();
    const hasForm   = await page.locator('.form-card').count();
    expect(hasActive + hasForm).toBeGreaterThan(0);
  });

  test('switches to History tab and shows past shifts or empty state', async ({ page }) => {
    await page.waitForSelector('.p-tabs, p-tabs', { timeout: 8_000 });
    await page.getByRole('tab', { name: /history/i }).first().click();
    // History renders .history-card items or an .empty-state
    await page.waitForSelector('.history-card, .empty-state', { timeout: 10_000 });
    const hasCards = await page.locator('.history-card').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasCards + hasEmpty).toBeGreaterThan(0);
  });
});
