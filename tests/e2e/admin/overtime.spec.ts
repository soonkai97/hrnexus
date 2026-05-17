import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Overtime Rules
 *
 * WHY these tests matter:
 *  - OT rules drive payroll multipliers for every shift over the threshold; a
 *    missing or broken rule means staff are under/over-paid silently.
 *  - The add-rule form must open cleanly and be closeable so managers can
 *    configure rules without reloading the page.
 *  - The rules table must load (or show an empty state) — a blank screen with
 *    no indication of status leaves managers guessing whether rules exist.
 */
test.describe('Admin — Overtime Rules', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/overtime');
    await page.waitForURL(/\/overtime/);
  });

  test('shows the overtime rules page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/overtime/i, { timeout: 8_000 });
  });

  test('shows the Add Rule button', async ({ page }) => {
    await expect(
      page.getByRole('button', { name: /add.*rule|new.*rule/i })
    ).toBeVisible({ timeout: 8_000 });
  });

  test('opens the Add Rule form when button is clicked', async ({ page }) => {
    await page.getByRole('button', { name: /add.*rule|new.*rule/i }).click();
    await expect(page.locator('.form-card')).toBeVisible({ timeout: 5_000 });
  });

  test('shows OT rules list or empty state after load', async ({ page }) => {
    // Either a rules table or an empty-state message must appear once loading finishes
    await page.waitForSelector(
      '.p-datatable-tbody tr, .empty-state, .skeleton-row',
      { timeout: 10_000 }
    );
    const hasRows  = await page.locator('.p-datatable-tbody tr').count();
    const hasEmpty = await page.locator('.empty-state').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });
});
