import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * Admin App — Reports & Payroll
 *
 * WHY these tests matter:
 *  - Reports are the primary tool for payroll processing; if the filter bar
 *    doesn't render or the Run Report button is broken, managers can't generate
 *    pay data and payroll is delayed.
 *  - The four stat cards (total shifts, hours, staff, labour cost) give an
 *    at-a-glance summary; missing cards mean managers must dig into raw rows.
 *  - The five result tabs (By Employee, Location, Role, Date, Shifts) must all
 *    be present after a run — a missing tab hides an entire reporting dimension.
 */
test.describe('Admin — Reports', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/reports');
    await page.waitForURL(/\/reports/);
  });

  test('shows the reports page heading', async ({ page }) => {
    await expect(page.locator('h1')).toContainText(/report/i, { timeout: 8_000 });
  });

  test('shows the filter bar with date range controls', async ({ page }) => {
    // .filter-bar is always visible and contains date pickers + multiselects
    await expect(page.locator('.filter-bar')).toBeVisible({ timeout: 8_000 });
  });

  test('shows stat summary cards after running a report', async ({ page }) => {
    await page.waitForSelector('.filter-bar', { timeout: 8_000 });
    // Click Run Report without date filters — returns all-time data
    await page.getByRole('button', { name: /run.*report|generate/i }).click();
    // Four stat cards appear once the query returns
    await page.waitForSelector('.stat-card', { timeout: 15_000 });
    const count = await page.locator('.stat-card').count();
    expect(count).toBeGreaterThanOrEqual(4);
  });

  test('shows result tabs after running a report', async ({ page }) => {
    await page.waitForSelector('.filter-bar', { timeout: 8_000 });
    await page.getByRole('button', { name: /run.*report|generate/i }).click();
    await page.waitForSelector('.stat-card', { timeout: 15_000 });
    // Result tabs: By Employee, By Location, By Role, By Date, Shift Detail
    await expect(page.getByRole('tab', { name: /employee|staff/i }).first()).toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole('tab', { name: /location/i }).first()).toBeVisible({ timeout: 5_000 });
  });
});
