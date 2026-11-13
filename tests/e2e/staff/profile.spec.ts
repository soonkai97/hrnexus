import { test, expect } from '@playwright/test';
import { loginStaff } from '../shared/helpers';

/**
 * Staff App — Profile & Push Notifications
 *
 * WHY these tests matter:
 *  - The profile page is the staff member's identity card; if the avatar, name,
 *    or staff ID are blank, staff can't verify they're logged into the right account.
 *  - The push notification toggle is the only way staff opt in to leave and other
 *    alerts on mobile; if it's missing or broken, staff miss real-time alerts.
 *  - The sign-out button must be present; its absence traps staff in the app.
 */
test.describe('Staff — Profile', () => {
  test.beforeEach(async ({ page }) => {
    await loginStaff(page);
    await page.goto('/profile');
    await page.waitForURL(/\/profile/);
  });

  test('shows the profile page with user avatar and name', async ({ page }) => {
    await expect(page.locator('.avatar-section')).toBeVisible({ timeout: 8_000 });
    // Avatar initials and full name are always rendered from the JWT payload.
    // Scope .user-name to .avatar-section to avoid matching the sidebar's span.user-name.
    await expect(page.locator('.avatar')).toBeVisible({ timeout: 5_000 });
    await expect(page.locator('.avatar-section .user-name, h2.user-name').first()).toBeVisible({ timeout: 5_000 });
  });

  test('shows staff info card with personal details', async ({ page }) => {
    // .info-card contains Staff ID, email, and other profile fields
    await expect(page.locator('.info-card')).toBeVisible({ timeout: 8_000 });
    await expect(page.locator('.info-row').first()).toBeVisible({ timeout: 5_000 });
  });

  /**
   * Push Notifications (Task 4)
   *
   * The profile page exposes a card to enable/disable browser push alerts.
   * In headless Playwright we verify the toggle button is present and operable;
   * actual subscription requires VAPID + service-worker — tested via presence only.
   */
  test('shows the push notification toggle section', async ({ page }) => {
    // Grant notification permission so the browser doesn't block the UI
    await page.context().grantPermissions(['notifications']);
    // The push card renders unconditionally — it shows "Enable" or "Disable" based on subscription state
    await expect(
      page.getByRole('button', { name: /enable.*notification|disable.*notification|push/i })
    ).toBeVisible({ timeout: 8_000 });
  });

  test('shows the sign-out button', async ({ page }) => {
    await expect(page.locator('.sign-out-wrap')).toBeVisible({ timeout: 8_000 });
    // Scope to .sign-out-wrap to avoid matching the sidebar's "Sign out" button
    await expect(
      page.locator('.sign-out-wrap').getByRole('button', { name: /sign.?out|logout/i })
    ).toBeVisible({ timeout: 5_000 });
  });
});
