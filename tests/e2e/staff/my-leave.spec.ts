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
    // annual, sick and unpaid are always there; a type with its own allowance (hospitalisation) adds a card
    expect(await cards.count()).toBeGreaterThanOrEqual(3);
    await expect(page.locator('.bal-card', { hasText: 'Hospitalisation' })).toBeVisible();
  });

  test('shows the Apply / History tab selector', async ({ page }) => {
    await expect(page.locator('.p-selectbutton')).toBeVisible({ timeout: 8_000 });
  });

  test('shows the leave application form in Apply tab', async ({ page }) => {
    // The Apply tab should be the default view (view signal defaults to 'apply')
    await page.waitForSelector('.p-selectbutton', { timeout: 8_000 });
    // Apply form renders as <p-card styleClass="form-card"> → class "form-card"
    await expect(page.locator('.form-card')).toBeVisible({ timeout: 8_000 });
  });

  // Regression guard: the Apply button must stay disabled until the required date/time fields are
  // filled (canSubmit), consistent with every other form. An always-enabled submit was the bug.
  test('Apply button is disabled until required date/time fields are filled', async ({ page }) => {
    await page.waitForSelector('.form-card', { timeout: 8_000 });
    // The single submit button lives in .form-actions (label is dynamic — target by container).
    const applyBtn = page.locator('.form-actions button');
    await expect(applyBtn).toBeVisible({ timeout: 5_000 });
    // Fresh form (only the default leave type) → start/end date+time are empty → disabled.
    await expect(applyBtn).toBeDisabled();
  });

  test('switches to History tab and shows records or empty state', async ({ page }) => {
    await page.waitForSelector('.p-selectbutton', { timeout: 8_000 });
    // PrimeNG 21 SelectButton renders options as plain <button> elements inside a role="group".
    // The second button is "Leave History" — click by position to avoid translated-text dependency.
    await page.locator('.p-selectbutton').getByRole('button').last().click();
    // History view uses card-based layout (styleClass="leave-card" / "empty-state"), not p-datatable.
    await page.waitForSelector('.leave-card, .empty-state', { timeout: 10_000 });
    const hasCards = await page.locator('.leave-card').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasCards + hasEmpty).toBeGreaterThan(0);
  });
});
