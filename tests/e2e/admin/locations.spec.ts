import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

test.describe('Admin — locations', () => {

  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/locations');
    await page.waitForURL(/\/locations/);
  });

  test('locations page loads and shows the Add Location button', async ({ page }) => {
    // The button is always in the page header — no conditional rendering
    await expect(page.getByRole('button', { name: /add location/i })).toBeVisible({ timeout: 8_000 });
  });

  test('locations list or empty state renders after load', async ({ page }) => {
    // Either cards/table rows or an empty-state message must appear
    await expect(
      page.locator('.location-card, .location-item, p-card, .empty-state, [class*="empty"]').first()
    ).toBeVisible({ timeout: 8_000 });
  });

  // Bulk import: the "Bulk Add" dialog lets an admin paste many location names at once.
  test('Bulk Add dialog opens with a paste textarea', async ({ page }) => {
    await page.getByRole('button', { name: /bulk add/i }).click();
    await expect(page.locator('.p-dialog textarea')).toBeVisible({ timeout: 5_000 });
  });

});
