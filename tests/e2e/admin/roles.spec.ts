import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Roles & Access Control
 *
 * WHY these tests matter:
 *  - Roles gate every guarded route; a missing or misconfigured role means
 *    staff see routes they shouldn't (or can't see routes they need).
 *  - System roles (admin, manager, employee) must always be present; the UI
 *    must prevent deleting them while allowing custom roles to be deleted.
 *  - The add-role form is the only way to create custom job titles; if it
 *    doesn't open, the venue can't onboard new role types.
 */
test.describe('Admin — Roles', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/roles');
    await page.waitForURL(/\/roles/);
  });

  test('shows the roles page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/role/i, { timeout: 8_000 });
  });

  test('shows the Add New Role button', async ({ page }) => {
    await expect(
      page.getByRole('button', { name: /add.*role|new.*role/i })
    ).toBeVisible({ timeout: 8_000 });
  });

  test('shows roles table with at least one role', async ({ page }) => {
    // System roles (admin, manager, employee) are always present
    await page.waitForSelector('.p-datatable-tbody tr', { timeout: 10_000 });
    const count = await page.locator('.p-datatable-tbody tr').count();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  test('opens the add role form when button is clicked', async ({ page }) => {
    // Wait for the button to be actionable first — the header now holds two buttons
    // (Bulk Add + Add New Role), and under heavy parallel load an immediate click can
    // race the page settling. getByRole's regex still matches only "Add New Role".
    const addBtn = page.getByRole('button', { name: /add.*role|new.*role/i });
    await expect(addBtn).toBeVisible({ timeout: 10_000 });
    await addBtn.click();
    // .add-card contains the inline form for creating a custom role
    await expect(page.locator('.add-card')).toBeVisible({ timeout: 5_000 });
  });

  // Bulk import: the "Bulk Add" dialog is an editable Name + Rate row grid.
  test('Bulk Add dialog opens with an editable Name/Rate grid', async ({ page }) => {
    const bulkBtn = page.getByRole('button', { name: /bulk add/i });
    await expect(bulkBtn).toBeVisible({ timeout: 10_000 });   // let the page settle first
    await bulkBtn.click();
    await expect(page.locator('.p-dialog .bulk-grid-row input').first()).toBeVisible({ timeout: 5_000 });
  });
});
