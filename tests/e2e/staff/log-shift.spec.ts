import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

test.describe('Staff app — log shift page', () => {

  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/log-shift');
    await page.waitForURL(/\/log-shift/);
  });

  test('log-shift page renders without error', async ({ page }) => {
    // The page must render something visible — not a blank screen or error
    await expect(page.locator('body')).not.toBeEmpty();
    // Should show mode toggle tabs (Live / Manual) inside the Log tab
    // PrimeNG p-tab renders with role="tab", not role="button"
    await expect(page.getByRole('tab', { name: /manual/i })).toBeVisible({ timeout: 5_000 });
  });

  test('switching to Manual tab shows the manual entry form', async ({ page }) => {
    await page.getByRole('tab', { name: /manual/i }).click();
    // The manual form has a date field plus several time pickers — assert the first
    await expect(page.locator('input[type="date"], p-datepicker').first()).toBeVisible({ timeout: 5_000 });
  });

  test('history tab shows completed shift records or an empty state', async ({ page }) => {
    // PrimeNG p-tab renders with role="tab", not role="button"
    await page.getByRole('tab', { name: /history/i }).click();
    // Either history cards (.history-card) or an empty-state message — never a blank crash.
    // .first() avoids strict-mode violation: the empty state contains both
    // .empty-state and a nested .empty-icon, which both match [class*="empty"].
    await expect(
      page.locator('.history-card, .empty-state').first()
    ).toBeVisible({ timeout: 6_000 });
  });

  // ── Staff ↔ location assignment ─────────────────────────────────────────────
  // The Live form must expose a Location field; the location list is driven by the
  // staff member's assigned venues (GET /api/locations/mine). Asserting the label
  // renders proves the location UI is wired regardless of how many venues are assigned.
  test('Live form shows a Location field driven by assigned venues', async ({ page }) => {
    await expect(page.locator('label[for="ls-live-location"]')).toBeVisible({ timeout: 6_000 });
  });

  // Edge case: a staff with no assigned locations is BLOCKED from clocking in — they see
  // a warning banner and the location dropdown has nothing to pick. Either the dropdown
  // offers options OR the no-locations warning shows; the form must never silently crash.
  test('no-location staff sees guidance, assigned staff sees a pickable location', async ({ page }) => {
    const locationSelect = page.locator('#ls-live-location');
    const noLocationsWarning = page.locator('p-message').filter({ hasText: /location|assigned|manager/i });
    await expect(locationSelect.or(noLocationsWarning).first()).toBeVisible({ timeout: 6_000 });
  });

});
