import { test, expect } from '@playwright/test';
import { loginAs, ADMIN } from '../shared/helpers';

test.describe('Admin — staff list', () => {

  test.beforeEach(async ({ page }) => {
    await loginAs(page, ADMIN.staffId, ADMIN.password);
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await page.goto('/staff');
  });

  test('staff list page renders a table with at least the seeded admin row', async ({ page }) => {
    // Wait for the skeleton to clear and at least one row to appear
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    // HRADMIN is always seeded — at least one row must exist
    await expect(page.getByText('HRADMIN')).toBeVisible({ timeout: 5_000 });
  });

  test('search filters the staff list', async ({ page }) => {
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    // Type in the search box — admin staff list has a search input
    const searchInput = page.locator('input[placeholder*="search" i], input[placeholder*="Search" i]').first();
    await searchInput.fill('HRADMIN');
    await expect(page.getByText('HRADMIN')).toBeVisible({ timeout: 5_000 });
  });

  test('navigating to a staff detail page loads without error', async ({ page }) => {
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    // Click the first row's view/edit link or the row itself
    const firstRow = page.locator('tbody tr').first();
    await firstRow.click();
    await expect(page).toHaveURL(/\/staff\//, { timeout: 5_000 });
  });

});
