import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Announcements
 *
 * WHY these tests matter:
 *  - Announcements are the primary broadcast channel to all staff; if the
 *    compose form doesn't open or save, staff miss critical notices.
 *  - Title and body validation must block empty submissions so nameless
 *    announcements don't reach the staff feed.
 *  - The paginator depends on totalPages computed; a broken paginator hides
 *    announcements that fall on page 2+.
 */
test.describe('Admin — Announcements', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/announcements');
    await page.waitForURL(/\/announcements/);
  });

  test('shows the announcements page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/announcement/i, { timeout: 8_000 });
  });

  test('shows the New Announcement button', async ({ page }) => {
    await expect(page.getByRole('button', { name: /new announcement/i })).toBeVisible({ timeout: 8_000 });
  });

  test('opens the compose form when New Announcement is clicked', async ({ page }) => {
    await page.getByRole('button', { name: /new announcement/i }).click();
    // Form card should appear with title and body inputs
    await expect(page.locator('.form-card')).toBeVisible({ timeout: 5_000 });
    await expect(page.locator('input[placeholder]').first()).toBeVisible({ timeout: 5_000 });
  });

  test('Save button is disabled until both title and body are filled', async ({ page }) => {
    await page.getByRole('button', { name: /new announcement/i }).click();
    await page.waitForSelector('.form-card', { timeout: 5_000 });

    // Empty form — Save must be disabled (isFormValid requires title + body)
    const saveBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(saveBtn).toBeDisabled({ timeout: 5_000 });

    // Fill body only — still disabled because title is blank
    // provideNoAutofill() stamps inputs readonly until the first mousedown;
    // .click() fires mousedown → removes readonly so .fill() can proceed.
    const bodyInput = page.locator('textarea').first();
    await bodyInput.click();
    await bodyInput.fill('Some body text');
    await expect(saveBtn).toBeDisabled();
  });

  test('creates an announcement and shows it in the list', async ({ page }) => {
    const title = `E2E Test — ${Date.now()}`;

    await page.getByRole('button', { name: /new announcement/i }).click();
    await page.waitForSelector('.form-card', { timeout: 5_000 });

    // provideNoAutofill() stamps inputs readonly until first mousedown — click first.
    const titleInput = page.locator('input[placeholder]').first();
    await titleInput.click();
    await titleInput.fill(title);

    const bodyInput = page.locator('textarea').first();
    await bodyInput.click();
    await bodyInput.fill('This is an automated E2E test announcement.');

    // Both fields filled — Save is now enabled
    const saveBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(saveBtn).toBeEnabled({ timeout: 3_000 });
    await saveBtn.click();

    // Form should close and list should contain the new item
    await expect(page.locator('.form-card')).not.toBeVisible({ timeout: 8_000 });
    await expect(page.locator(`text=${title}`)).toBeVisible({ timeout: 8_000 });
  });

  test('shows announcements list or empty state', async ({ page }) => {
    await page.waitForSelector(
      '.skeleton-row, .p-datatable-tbody tr, .empty-state',
      { timeout: 12_000 }
    );
    const hasRows   = await page.locator('.p-datatable-tbody tr').count();
    const hasEmpty  = await page.locator('.empty-state').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });
});
