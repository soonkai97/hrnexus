import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

/**
 * Staff App — My Leave
 *
 * WHY these tests matter:
 *  - Leave application is the core self-service flow for staff; a broken form
 *    forces staff to call managers, creating unnecessary overhead.
 *  - Balance cards (annual, sick, unpaid) must display the correct remaining
 *    days — an incorrect count causes staff to over-apply and get rejected.
 *  - The Apply/History tab switch must work so staff can check the status of
 *    their existing requests without navigating away.
 */
test.describe('Staff — My Leave', () => {
  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/my-leave');
    await page.waitForURL(/\/my-leave/);
  });

  test('shows the leave page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/leave/i, { timeout: 8_000 });
  });

  test('shows leave balance cards (annual, sick, unpaid)', async ({ page }) => {
    await page.waitForSelector('.bal-card', { timeout: 10_000 });
    const cards = page.locator('.bal-card');
    expect(await cards.count()).toBe(3); // annual, sick, unpaid
  });

  test('shows the Apply / History tab selector', async ({ page }) => {
    await expect(page.locator('.p-selectbutton')).toBeVisible({ timeout: 8_000 });
  });

  test('shows the leave application form in Apply tab', async ({ page }) => {
    // The Apply tab should be the default view
    await page.waitForSelector('.p-selectbutton', { timeout: 8_000 });
    // Apply form fields should be visible (leave type, start date, end date)
    const formSection = page.locator('text=/apply|leave type|start date/i').first();
    await expect(formSection).toBeVisible({ timeout: 8_000 });
  });

  test('switches to History tab and shows records or empty state', async ({ page }) => {
    await page.waitForSelector('.p-selectbutton', { timeout: 8_000 });
    // Click the History option in the selectbutton
    await page.locator('.p-selectbutton').getByText(/history/i).click();
    await page.waitForSelector(
      '.p-datatable-tbody tr, .empty-state, text=/no leave/i',
      { timeout: 10_000 }
    );
    const hasRows   = await page.locator('.p-datatable-tbody tr').count();
    const hasEmpty  = await page.locator('.empty-state, text=/no leave/i').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });
});
