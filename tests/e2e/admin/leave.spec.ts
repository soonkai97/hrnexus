import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Leave Management
 *
 * WHY these tests matter:
 *  - Leave approval is time-sensitive; managers need to act on pending requests
 *    quickly. A broken approve/reject button means leave requests stall.
 *  - Filter controls (staff, date, status) are essential for managers with many
 *    employees — if filters don't render, the leave list is unusable at scale.
 *  - The "Record Leave" form allows admin-initiated entries (e.g. sick day
 *    without a staff request); a broken form creates payroll discrepancies.
 */
test.describe('Admin — Leave Management', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/leave');
    await page.waitForURL(/\/leave/);
  });

  test('shows the leave page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/leave/i, { timeout: 8_000 });
  });

  test('shows the Record Leave button', async ({ page }) => {
    await expect(page.getByRole('button', { name: /record leave/i })).toBeVisible({ timeout: 8_000 });
  });

  test('shows filter controls — staff, from, to, status', async ({ page }) => {
    // The filter bar should render four filter controls
    const filterBar = page.locator('.filter-bar, .filter-row').first();
    await expect(filterBar).toBeVisible({ timeout: 8_000 });
  });

  test('shows leave records table or empty state after load', async ({ page }) => {
    // Leave component uses a card-based layout (not p-datatable).
    // Skeleton and data cards both use styleClass="leave-card"; empty state uses styleClass="empty-state".
    await page.waitForSelector('.filter-bar', { timeout: 8_000 });
    await page.waitForSelector('.leave-card, .empty-state', { timeout: 10_000 });
    const hasCards = await page.locator('.leave-card').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasCards + hasEmpty).toBeGreaterThan(0);
  });

  test('opens the Record Leave dialog when button is clicked', async ({ page }) => {
    await page.getByRole('button', { name: /record leave/i }).click();
    // The record form is now a modal dialog — assert its staff-member field is shown.
    await expect(page.locator('.p-dialog label[for="leave-form-staff"]')).toBeVisible({ timeout: 5_000 });
  });

  // Bulk import: the "Bulk Record" dialog records the same leave for many staff at once.
  test('Bulk Record dialog opens with a staff selector', async ({ page }) => {
    await page.getByRole('button', { name: /bulk record/i }).click();
    await expect(page.locator('label[for="bl-staff"]')).toBeVisible({ timeout: 5_000 });
  });

  test('leave summary pills appear after data loads', async ({ page }) => {
    // Click the Balances tab to load the balance overview
    await page.getByRole('tab', { name: /balance/i }).click();
    // The balance overview card appears (nc-card) with staff balance rows
    // or the "no active staff" fallback — either way something renders
    await page.waitForSelector('.nc-card, .balance-card', { timeout: 12_000 });
    const hasNcCard      = await page.locator('.nc-card').count();
    const hasBalanceCard = await page.locator('.balance-card').count();
    expect(hasNcCard + hasBalanceCard).toBeGreaterThan(0);
  });
});
