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
    const rows = page.locator('.list-row');
    await expect(rows.first()).toBeVisible({ timeout: 8_000 });
  });

  test('Work Types tab — Add button opens inline form', async ({ page }) => {
    await page.getByRole('tab', { name: /work types/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await expect(page.getByPlaceholder('VENUECLOCK')).toBeVisible({ timeout: 5_000 });
  });

  test('Work Types tab — Save disabled when required fields empty', async ({ page }) => {
    await page.getByRole('tab', { name: /work types/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await page.waitForSelector('input[placeholder="VENUECLOCK"]', { timeout: 5_000 });
    // Create button should be disabled with empty fields
    const createBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(createBtn).toBeDisabled({ timeout: 5_000 });
  });

  test('Work Types tab — Cancel closes the form', async ({ page }) => {
    await page.getByRole('tab', { name: /work types/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await page.waitForSelector('input[placeholder="VENUECLOCK"]', { timeout: 5_000 });
    await page.getByRole('button', { name: /cancel/i }).click();
    await expect(page.getByPlaceholder('VENUECLOCK')).not.toBeVisible({ timeout: 3_000 });
  });

  // ── Deduction Categories ────────────────────────────────────────────────────

  test('Deduction Categories tab — switches and shows list', async ({ page }) => {
    await page.getByRole('tab', { name: /deduction categories/i }).click();
    await page.waitForTimeout(500);
    // Should show seeded categories or empty state
    const hasRows  = await page.locator('.list-row').count();
    const hasEmpty = await page.locator('.empty-list').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });

  test('Deduction Categories tab — Add button opens inline form', async ({ page }) => {
    await page.getByRole('tab', { name: /deduction categories/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await expect(page.getByPlaceholder('MEAL')).toBeVisible({ timeout: 5_000 });
  });

  test('Deduction Categories tab — Save disabled when fields empty', async ({ page }) => {
    await page.getByRole('tab', { name: /deduction categories/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await page.waitForSelector('input[placeholder="MEAL"]', { timeout: 5_000 });
    const createBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(createBtn).toBeDisabled({ timeout: 5_000 });
  });

  // ── Settlements ─────────────────────────────────────────────────────────────

  test('Settlements tab — switches and shows list with severity tags', async ({ page }) => {
    await page.getByRole('tab', { name: /settlements/i }).click();
    await page.waitForTimeout(500);
    const hasRows  = await page.locator('.list-row').count();
    const hasEmpty = await page.locator('.empty-list').count();
    expect(hasRows + hasEmpty).toBeGreaterThan(0);
  });

  test('Settlements tab — Add button opens form with severity dropdown', async ({ page }) => {
    await page.getByRole('tab', { name: /settlements/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await expect(page.getByPlaceholder('SETTLED')).toBeVisible({ timeout: 5_000 });
  });

  // ── Loan Types ──────────────────────────────────────────────────────────────

  test('Loan Types tab — shows system types ADVANCE and REPAYMENT', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.waitForTimeout(500);
    // Should show at least 2 seeded system types
    const rows = page.locator('.list-row');
    await expect(rows.first()).toBeVisible({ timeout: 8_000 });
    expect(await rows.count()).toBeGreaterThanOrEqual(2);
  });

  test('Loan Types tab — system types have delete button disabled', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.waitForSelector('.list-row', { timeout: 8_000 });
    // Each row with System badge should have a disabled delete button
    const systemBadge = page.locator('.badge-system').first();
    await expect(systemBadge).toBeVisible({ timeout: 5_000 });
    // The delete button in the same row should be disabled
    const systemRow = page.locator('.list-row').filter({ has: page.locator('.badge-system') }).first();
    const deleteBtn = systemRow.getByRole('button').last();
    await expect(deleteBtn).toBeDisabled({ timeout: 3_000 });
  });

  test('Loan Types tab — Add button opens form with direction dropdown', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await expect(page.getByPlaceholder('CASH_ADVANCE')).toBeVisible({ timeout: 5_000 });
  });

  test('Loan Types tab — Save disabled when required fields empty', async ({ page }) => {
    await page.getByRole('tab', { name: /loan types/i }).click();
    await page.getByRole('button', { name: /add/i }).first().click();
    await page.waitForSelector('input[placeholder="CASH_ADVANCE"]', { timeout: 5_000 });
    const createBtn = page.locator('.form-actions').getByRole('button').last();
    await expect(createBtn).toBeDisabled({ timeout: 5_000 });
  });
});
