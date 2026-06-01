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

  // ── Staff ↔ location assignment ─────────────────────────────────────────────
  // The Add Staff form must let an admin assign the work location(s) a staff member
  // is allowed to clock in at, alongside roles.
  test('Add Staff form exposes a work-location assignment field', async ({ page }) => {
    await page.getByRole('button', { name: /add staff/i }).first().click();
    // The Locations multiselect's label is bound to for="st-locations" — deterministic
    // regardless of how many venues exist in the seed data.
    await expect(page.locator('label[for="st-locations"]')).toBeVisible({ timeout: 5_000 });
  });

  // ── Bulk actions ─────────────────────────────────────────────────────────────
  // Entering bulk mode reveals a selection column + an action bar; opening the
  // "Assign locations" action shows its dialog. Non-destructive — no action is applied.
  test('bulk mode reveals the action bar and the assign-locations dialog', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    await page.getByRole('button', { name: /bulk select/i }).first().click();
    // Select-all header checkbox appears in bulk mode, then the action buttons enable.
    await page.locator('thead .p-checkbox, thead p-checkbox').first().click();
    const assignLocations = page.getByRole('button', { name: /assign locations/i });
    await expect(assignLocations).toBeVisible({ timeout: 5_000 });
    await assignLocations.click();
    await expect(page.locator('label[for="bulk-locations"]')).toBeVisible({ timeout: 5_000 });
  });

  // ── Bulk import (editable row grid) ──────────────────────────────────────────
  test('Import dialog opens with an editable row grid', async ({ page }) => {
    const importBtn = page.getByRole('button', { name: 'Import' }).first();
    await expect(importBtn).toBeVisible({ timeout: 10_000 });
    await importBtn.click();
    await expect(page.locator('.p-dialog .import-grid-row input').first()).toBeVisible({ timeout: 5_000 });
  });

  // End-to-end: fill one valid row (Staff ID + name + role) and one invalid row (no name) →
  // partial success. Uses a unique Staff ID so repeated runs don't collide.
  test('importing rows shows a per-row results table (partial success)', async ({ page }) => {
    const importBtn = page.getByRole('button', { name: 'Import' }).first();
    await expect(importBtn).toBeVisible({ timeout: 10_000 });
    await importBtn.click();
    const rows = page.locator('.p-dialog .import-grid-row');
    await rows.first().waitFor({ timeout: 5_000 });

    const uid = 'E2E' + Date.now().toString().slice(-7);
    // Inputs start readonly (anti-autofill) until interacted with — click before fill.
    const set = async (loc: typeof rows, val: string) => { await loc.click(); await loc.fill(val); };
    // Row 1 — valid: Staff ID + first name + password + a role (inputs: id,first,last,email,password)
    await set(rows.nth(0).locator('input').nth(0), uid);
    await set(rows.nth(0).locator('input').nth(1), 'E2E');
    await set(rows.nth(0).locator('input').nth(4), 'Passw0rd!');
    await rows.nth(0).locator('p-multiselect').click();
    await page.getByRole('option', { name: 'STAFF', exact: true }).click();
    await page.keyboard.press('Escape');
    // Row 2 — invalid: Staff ID but no first name
    await set(rows.nth(1).locator('input').nth(0), 'BAD' + uid);

    await page.locator('.p-dialog').getByRole('button', { name: 'Import' }).click();
    // Results table renders with both an Active (success) and a Failed tag (partial success).
    await expect(page.locator('.p-dialog').getByText(uid).first()).toBeVisible({ timeout: 8_000 });
    await expect(page.locator('.p-dialog').getByText(/failed/i).first()).toBeVisible({ timeout: 5_000 });
  });

});
