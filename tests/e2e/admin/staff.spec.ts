import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

test.describe('Admin — staff list', () => {

  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/staff');
    await page.waitForURL(/\/staff/);
  });

  test('staff list page renders a table with at least the seeded admin row', async ({ page }) => {
    // Wait for the skeleton to clear and at least one row to appear
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    // HRADMIN is always seeded — at least one row must exist
    await expect(page.getByText('HRADMIN')).toBeVisible({ timeout: 5_000 });
  });

  test('search filters the staff list', async ({ page }) => {
    // Wait for the table to fully load first
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    // The search input is inside .search-wrap — find it directly
    const searchInput = page.locator('.search-wrap input').first();
    await expect(searchInput).toBeVisible({ timeout: 5_000 });
    // The no-autofill feature stamps inputs readonly until first interaction,
    // so .fill() (which requires an editable element) deadlocks. Click first —
    // mousedown/focus strips the temporary readonly — then fill.
    await searchInput.click();
    await searchInput.fill('HRADMIN');
    // Server-side search has a 300ms debounce; allow up to 6s for API to respond
    await expect(page.getByText('HRADMIN')).toBeVisible({ timeout: 6_000 });
  });

  test('navigating to a staff detail page loads without error', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    // Click the name link (routerLink) in the first row — not the row itself
    const firstNameLink = page.locator('tbody tr').first().locator('a.staff-name-link');
    await firstNameLink.click();
    await expect(page).toHaveURL(/\/staff\//, { timeout: 5_000 });
  });

});
