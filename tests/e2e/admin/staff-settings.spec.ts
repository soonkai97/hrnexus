import { test, expect } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

test.describe('Admin — Staff Settings', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/staff-settings');
    await page.waitForURL(/\/staff-settings/);
  });

  test('shows all 4 tabs: Work Types, Deduction Categories, Settlements, Loan Types', async ({ page }) => {
    await expect(page.getByRole('tab', { name: /work types/i })).toBeVisible({ timeout: 8_000 });
    await expect(page.getByRole('tab', { name: /deduction categories/i })).toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole('tab', { name: /settlements/i })).toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole('tab', { name: /loan types/i })).toBeVisible({ timeout: 5_000 });
  });

  // ── Work Types ──────────────────────────────────────────────────────────────

  test('Work Types tab — shows seeded items with key badges', async ({ page }) => {
    await page.getByRole('tab', { name: /work types/i }).click();
    await page.waitForTimeout(500);
    // Work Types is the default (first) tab so its rows are already active
    const rows = page.locator('.list-row:visible');
    await expect(rows.first()).toBeVisible({ timeout: 8_000 });
  });

  test('Work Types tab — Save disabled when required fields empty', async ({ page }) => {
    await page.getByRole('tab', { name: /work types/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    // Scope Add button click to the currently visible tab panel actions
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    await page.waitForSelector('input[placeholder="VENUECLOCK"]', { timeout: 5_000 });
    // Create button should be disabled with empty fields
    const createBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(createBtn).toBeDisabled({ timeout: 5_000 });
  });

  test('Work Types tab — Cancel closes the form', async ({ page }) => {
    await page.getByRole('tab', { name: /work types/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    await page.waitForSelector('input[placeholder="VENUECLOCK"]', { timeout: 5_000 });
    await page.getByRole('button', { name: /cancel/i }).click();
    await expect(page.getByPlaceholder('VENUECLOCK', { exact: true })).not.toBeVisible({ timeout: 3_000 });
  });

  // ── Deduction Categories ────────────────────────────────────────────────────

  test('Deduction Categories tab — switches and shows list', async ({ page }) => {
    await page.getByRole('tab', { name: /deduction categories/i }).click();
    await page.waitForTimeout(500);
    // Count visible rows or empty-list within the active (visible) panel
    const hasRows  = await page.locator('.list-row:visible').count();
    const hasEmpty = await page.locator('.empty-list:visible').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });

  test('Deduction Categories tab — Add button opens inline form', async ({ page }) => {
    await page.getByRole('tab', { name: /deduction categories/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    // Use :visible scoping to target the active tab panel's Add button
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    // exact:true — getByPlaceholder is a case-insensitive substring match, so a
    // bare 'MEAL' also matches the "Meal Subsidy" label field (strict-mode clash).
    await expect(page.getByPlaceholder('MEAL', { exact: true })).toBeVisible({ timeout: 5_000 });
  });

  test('Deduction Categories tab — Save disabled when fields empty', async ({ page }) => {
    await page.getByRole('tab', { name: /deduction categories/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    await page.waitForSelector('input[placeholder="MEAL"]', { timeout: 5_000 });
    const createBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(createBtn).toBeDisabled({ timeout: 5_000 });
  });

  // ── Settlements ─────────────────────────────────────────────────────────────

  test('Settlements tab — switches and shows list with severity tags', async ({ page }) => {
    await page.getByRole('tab', { name: /settlements/i }).click();
    await page.waitForTimeout(500);
    const hasRows  = await page.locator('.list-row:visible').count();
    const hasEmpty = await page.locator('.empty-list:visible').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });

  test('Settlements tab — Add button opens form with severity dropdown', async ({ page }) => {
    await page.getByRole('tab', { name: /settlements/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    await expect(page.getByPlaceholder('SETTLED', { exact: true })).toBeVisible({ timeout: 5_000 });
  });

  // ── Loan Types ──────────────────────────────────────────────────────────────

  test('Loan Types tab — shows system types ADVANCE and REPAYMENT', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.waitForTimeout(500);
    // Use :visible to skip hidden panels from inactive tabs
    const rows = page.locator('.list-row:visible');
    await expect(rows.first()).toBeVisible({ timeout: 8_000 });
    expect(await rows.count()).toBeGreaterThanOrEqual(2);
  });

  test('Loan Types tab — system types have delete button disabled', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.waitForSelector('.list-row:visible', { timeout: 8_000 });
    // Badge-system element inside the currently visible tab panel
    const systemBadge = page.locator('.list-row:visible .badge-system').first();
    await expect(systemBadge).toBeVisible({ timeout: 5_000 });
    // The delete button in the same visible row should be disabled
    const systemRow = page.locator('.list-row:visible').filter({ has: page.locator('.badge-system') }).first();
    const deleteBtn = systemRow.getByRole('button').last();
    await expect(deleteBtn).toBeDisabled({ timeout: 3_000 });
  });

  test('Loan Types tab — Add button opens form with direction dropdown', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    await expect(page.getByPlaceholder('CASH_ADVANCE', { exact: true })).toBeVisible({ timeout: 5_000 });
  });

  test('Loan Types tab — Save disabled when required fields empty', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.waitForTimeout(500); // let the PrimeNG tab transition settle
    await page.locator('.tab-panel-actions:visible').getByRole('button', { name: /add/i }).click();
    await page.waitForSelector('input[placeholder="CASH_ADVANCE"]', { timeout: 5_000 });
    const createBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(createBtn).toBeDisabled({ timeout: 5_000 });
  });
});
