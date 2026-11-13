import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

test.describe('Admin — staff list', () => {

  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/staff');
    await page.waitForURL(/\/staff/);
  });

  test('staff list page renders a table with at least one row', async ({ page }) => {
    // Wait for the skeleton to clear and at least one row to appear
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    expect(await page.locator('tbody tr').count()).toBeGreaterThanOrEqual(1);
  });

  // HRADMIN (seed data) is hidden from the roster by default; the Show hidden toggle reveals it.
  test('seed accounts are hidden by default and appear with Show hidden', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    const searchInput = page.locator('.search-wrap input').first();
    await searchInput.click();
    await searchInput.fill('HRADMIN');
    await expect(page.getByText('HRADMIN')).toHaveCount(0, { timeout: 6_000 });
    await page.getByRole('button', { name: /show hidden/i }).click();
    await expect(page.getByText('HRADMIN').first()).toBeVisible({ timeout: 6_000 });
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
    await searchInput.fill('TESTSTAFF01');
    // Server-side search has a 300ms debounce; allow up to 6s for API to respond
    await expect(page.getByText('TESTSTAFF01').first()).toBeVisible({ timeout: 6_000 });
  });

  test('navigating to a staff detail page loads without error', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    // Click the name link (routerLink) in the first row — not the row itself
    const firstNameLink = page.locator('tbody tr').first().locator('a.staff-name-link');
    await firstNameLink.click();
    await expect(page).toHaveURL(/\/staff\//, { timeout: 5_000 });
  });

  // ── Bulk actions ─────────────────────────────────────────────────────────────
  // Entering bulk mode reveals a selection column + an action bar; opening the
  // "Assign jobs" action shows its dialog. Non-destructive — no action is applied.
  test('bulk mode reveals the action bar and the assign-jobs dialog', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    await page.getByRole('button', { name: /bulk select/i }).first().click();
    // Select-all header checkbox appears in bulk mode, then the action buttons enable.
    await page.locator('thead .p-checkbox, thead p-checkbox').first().click();
    const assignRoles = page.getByRole('button', { name: /assign jobs/i });
    await expect(assignRoles).toBeVisible({ timeout: 5_000 });
    await assignRoles.click();
    await expect(page.locator('label[for="bulk-roles"]')).toBeVisible({ timeout: 5_000 });
  });

});
