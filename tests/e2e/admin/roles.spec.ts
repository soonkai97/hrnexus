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
 *  - The add-role form is the only way to create custom roles; if it
 *    doesn't open, the venue can't onboard new role types.
 */
test.describe('Admin — Roles', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/roles');
    await page.waitForURL(/\/roles/);
  });

  test('shows the roles page heading (the UI calls roles "Roles")', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/role|role/i, { timeout: 8_000 });
  });

  test('shows the Add New Role button', async ({ page }) => {
    await expect(
      page.getByRole('button', { name: /add.*(role|role)|new.*(role|role)/i }),
    ).toBeVisible({ timeout: 8_000 });
  });

  test('shows the roles as a table with at least one role, and as cards when chosen', async ({
    page,
  }) => {
    // System roles (admin, manager, employee) are always present
    await page.waitForSelector('.p-datatable-tbody tr', { timeout: 10_000 });
    expect(await page.locator('.p-datatable-tbody tr').count()).toBeGreaterThanOrEqual(1);

    await page.getByRole('button', { name: 'Card view' }).click();
    await page.waitForSelector('.nc-card', { timeout: 10_000 });
    expect(await page.locator('.nc-card').count()).toBeGreaterThanOrEqual(1);
  });

  test('opens the add role dialog when button is clicked', async ({ page }) => {
    const addBtn = page.getByRole('button', { name: /add.*(role|role)|new.*(role|role)/i });
    await expect(addBtn).toBeVisible({ timeout: 10_000 });
    await addBtn.click();
    // The create form is now a modal dialog — assert its name input is shown.
    await expect(page.locator('.p-dialog #rl-role-name')).toBeVisible({ timeout: 5_000 });
  });
});
