import { test, expect } from '@playwright/test';
import { loginAs, ADMIN } from '../shared/helpers';

test.describe('Admin — locations', () => {

  test.beforeEach(async ({ page }) => {
    await loginAs(page, ADMIN.staffId, ADMIN.password);
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await page.goto('/locations');
  });

  test('locations page loads and shows the list', async ({ page }) => {
    // Wait for either a table or an empty-state message — either is a healthy render
    await expect(
      page.locator('p-datatable, table, .empty-state')
    ).toBeVisible({ timeout: 8_000 });
  });

  test('Add Location button is visible for admins', async ({ page }) => {
    await expect(page.locator('p-datatable, table, .empty-state')).toBeVisible({ timeout: 8_000 });
    await expect(page.getByRole('button', { name: /add location/i })).toBeVisible();
  });

});
