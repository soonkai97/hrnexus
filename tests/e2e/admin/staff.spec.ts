import { test, expect, type Page } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/** Switch the staff list to the card grid (the default is the list, i.e. the table). */
async function useCards(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Card view' }).click();
  await page.waitForSelector('.nc-card', { timeout: 8_000 });
}

test.describe('Admin — staff list', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/staff');
    await page.waitForURL(/\/staff/);
  });

  test('staff list opens as a table with at least one row', async ({ page }) => {
    // Wait for the skeleton to clear and at least one row to appear
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    expect(await page.locator('tbody tr').count()).toBeGreaterThanOrEqual(1);
    await expect(page.getByRole('button', { name: 'Table view' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  test('the card view shows the same list, and the choice is remembered after a reload', async ({
    page,
  }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    const rows = await page.locator('tbody tr').count();

    await useCards(page);
    expect(await page.locator('.nc-card').count(), 'the cards list the same page of people').toBe(
      rows,
    );

    await page.reload();
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    await expect(page.getByRole('button', { name: 'Card view' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );

    // Back to the list for the next visit.
    await page.getByRole('button', { name: 'Table view' }).click();
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
  });

  // HRADMIN (seed data) is hidden from the roster by default; the Show hidden toggle reveals it.
  test('seed accounts are hidden by default and appear with Show hidden', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    const searchInput = page.locator('.search-wrap input').first();
    await searchInput.click();
    await searchInput.fill('HRADMIN');
    await expect(page.getByText('HRADMIN')).toHaveCount(0, { timeout: 6_000 });
    await page.getByRole('button', { name: /show hidden/i }).click();
    await expect(page.getByText('HRADMIN').first()).toBeVisible({ timeout: 6_000 });
  });

  test('search filters the staff list', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    // The search input is inside .search-wrap — find it directly
    const searchInput = page.locator('.search-wrap input').first();
    await expect(searchInput).toBeVisible({ timeout: 5_000 });
    // The no-autofill feature stamps inputs readonly until first interaction,
    // so .fill() (which requires an editable element) deadlocks. Click first —
    // mousedown/focus strips the temporary readonly — then fill.
    await searchInput.click();
    await searchInput.fill('TESTSTAFF01');
    // Server-side search has a 300ms debounce; allow up to 6s for API to respond
    await expect(page.getByText('TESTSTAFF01').first()).toBeVisible({ timeout: 6_000 });
  });

  test('navigating to a staff detail page loads without error', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    // Click the name link (routerLink) in the first row — not the row itself
    await page.locator('tbody tr').first().locator('a.staff-name-link').click();
    await expect(page).toHaveURL(/\/staff\//, { timeout: 5_000 });
  });

  // ── Bulk actions ─────────────────────────────────────────────────────────────
  // Entering bulk mode reveals a selection column + an action bar; opening the
  // "Assign roles" action shows its dialog. Non-destructive — no action is applied.
  test('bulk mode reveals the action bar and the assign-roles dialog', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    await page
      .getByRole('button', { name: /bulk select/i })
      .first()
      .click();
    // Select-all header checkbox appears in bulk mode, then the action buttons enable.
    const selectAll = page.locator('thead .p-checkbox, thead p-checkbox').first();
    await expect(selectAll).toBeVisible({ timeout: 5_000 });
    await selectAll.click();
    const assignRoles = page.getByRole('button', { name: /assign roles/i });
    await expect(assignRoles).toBeVisible({ timeout: 5_000 });
    await assignRoles.click();
    await expect(page.locator('label[for="bulk-roles"]')).toBeVisible({ timeout: 5_000 });
  });

  test('bulk mode in the card view puts a checkbox on each card', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    await useCards(page);
    await page
      .getByRole('button', { name: /bulk select/i })
      .first()
      .click();
    await page.locator('.nc-card p-checkbox').first().click();
    await expect(page.locator('.nc-card.is-selected')).toHaveCount(1);
    await expect(page.getByRole('button', { name: /assign roles/i })).toBeEnabled({
      timeout: 5_000,
    });
  });

  test('a card links to the staff detail page', async ({ page }) => {
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    await useCards(page);
    await page.locator('.nc-card').first().locator('a.nc-card-title').click();
    await expect(page).toHaveURL(/\/staff\//, { timeout: 5_000 });
  });
});
