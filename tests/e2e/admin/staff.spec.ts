import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Staff Management
 *
 * WHY these tests matter:
 *  - Staff list is the primary management view; a blank table or broken search
 *    means managers can't find a specific employee quickly during a busy shift.
 *  - The Add Staff form is the only way to onboard new employees; if it doesn't
 *    open or validation silently passes, a manager could submit an incomplete record.
 *  - Debounced search (added in latest commit) should filter without API hammering.
 */
test.describe('Admin — Staff Management', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/staff');
    await page.waitForURL(/\/staff/);
  });

  test('shows the staff list page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/staff/i, { timeout: 8_000 });
  });

  test('shows the Add Staff button', async ({ page }) => {
    await expect(page.getByRole('button', { name: /add staff/i })).toBeVisible({ timeout: 8_000 });
  });

  test('opens the Add Staff form when button is clicked', async ({ page }) => {
    await page.getByRole('button', { name: /add staff/i }).click();
    // Form card should appear with Staff ID input
    await expect(page.getByPlaceholder(/nc1001/i)).toBeVisible({ timeout: 5_000 });
  });

  test('shows a search input for filtering staff', async ({ page }) => {
    const searchInput = page.locator('input[type="search"], input[placeholder*="earch"]').first();
    await expect(searchInput).toBeVisible({ timeout: 8_000 });
  });

  test('shows at least one staff row or an empty state', async ({ page }) => {
    // Wait for loading to finish (skeleton replaced by table or empty message)
    await page.waitForSelector('.skeleton-row, .p-datatable-tbody tr, .empty-state', { timeout: 10_000 });
    const hasRows   = await page.locator('.p-datatable-tbody tr').count();
    const hasEmpty  = await page.locator('.empty-state').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });

  test('Add Staff form shows validation error when submitted empty', async ({ page }) => {
    await page.getByRole('button', { name: /add staff/i }).click();
    await page.waitForSelector('input[placeholder*="NC"]', { timeout: 5_000 });
    // Click save without filling anything
    await page.getByRole('button', { name: /^save$/i }).click();
    // PrimeNG message or toast should appear
    await expect(
      page.locator('.p-message, [class*="toast"], [class*="error"]').first()
    ).toBeVisible({ timeout: 5_000 });
  });
});
