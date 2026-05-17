import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Location Management
 *
 * WHY these tests matter:
 *  - Locations are the clock-in anchors for every shift; if a location card
 *    doesn't render, staff can't be assigned to shifts there.
 *  - The add-location form is the only way to create new venues; a broken form
 *    blocks the entire onboarding process for new sites.
 *  - Each location card must show its name clearly so managers can identify
 *    venues quickly during a busy rostering session.
 */
test.describe('Admin — Locations', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/locations');
    await page.waitForURL(/\/locations/);
  });

  test('shows the locations page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/location/i, { timeout: 8_000 });
  });

  test('shows the Add Location button', async ({ page }) => {
    await expect(
      page.getByRole('button', { name: /add.*location|new.*location/i })
    ).toBeVisible({ timeout: 8_000 });
  });

  test('shows at least one location card or a loading/empty state', async ({ page }) => {
    await page.waitForSelector(
      '.loc-card, .loading-card, .empty-state',
      { timeout: 10_000 }
    );
    const hasCards   = await page.locator('.loc-card').count();
    const hasLoading = await page.locator('.loading-card').count();
    const hasEmpty   = await page.locator('.empty-state').count();
    expect(hasCards + hasLoading + hasEmpty).toBeGreaterThan(0);
  });

  test('opens the add location form when button is clicked', async ({ page }) => {
    await page.getByRole('button', { name: /add.*location|new.*location/i }).click();
    await expect(page.locator('.form-card')).toBeVisible({ timeout: 5_000 });
  });
});
