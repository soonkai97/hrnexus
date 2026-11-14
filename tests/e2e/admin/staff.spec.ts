import { test, expect, type Page } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/** Switch the staff list to the table (the default is the card grid). */
async function useTable(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Table view' }).click();
  await page.waitForSelector('tbody tr', { timeout: 8_000 });
}

test.describe('Admin — staff list', () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    await page.goto('/staff');
    await page.waitForURL(/\/staff/);
  });

  test('staff list opens as a grid of cards with at least one person', async ({ page }) => {
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    expect(await page.locator('.nc-card').count()).toBeGreaterThanOrEqual(1);
    await expect(page.getByRole('button', { name: 'Card view' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  test('the table view shows the same list, and the choice is remembered after a reload', async ({
    page,
  }) => {
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    const cards = await page.locator('.nc-card').count();

    await useTable(page);
    await expect(page.locator('p-datatable, table')).toBeVisible({ timeout: 8_000 });
    expect(await page.locator('tbody tr').count(), 'the table lists the same page of people').toBe(
      cards,
    );

    await page.reload();
    await page.waitForSelector('tbody tr', { timeout: 8_000 });
    await expect(page.getByRole('button', { name: 'Table view' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );

    // Back to cards for the next visit.
    await page.getByRole('button', { name: 'Card view' }).click();
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
  });

  // HRADMIN (seed data) is hidden from the roster by default; the Show hidden toggle reveals it.
  test('seed accounts are hidden by default and appear with Show hidden', async ({ page }) => {
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    const searchInput = page.locator('.search-wrap input').first();
    await searchInput.click();
    await searchInput.fill('HRADMIN');
    await expect(page.getByText('HRADMIN')).toHaveCount(0, { timeout: 6_000 });
    await page.getByRole('button', { name: /show hidden/i }).click();
    await expect(page.getByText('HRADMIN').first()).toBeVisible({ timeout: 6_000 });
  });

  test('search filters the staff list', async ({ page }) => {
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
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
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    // Click the name link (routerLink) on the first card — not the card itself
    await page.locator('.nc-card').first().locator('a.nc-card-title').click();
    await expect(page).toHaveURL(/\/staff\//, { timeout: 5_000 });
  });

  // ── Bulk actions ─────────────────────────────────────────────────────────────
  // Entering bulk mode reveals a selection checkbox on each card + an action bar; opening the
  // "Assign roles" action shows its dialog. Non-destructive — no action is applied.
  test('bulk mode on cards reveals the action bar and the assign-roles dialog', async ({
    page,
  }) => {
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    await page
      .getByRole('button', { name: /bulk select/i })
      .first()
      .click();
    await page.locator('.nc-card p-checkbox').first().click();
    await expect(page.locator('.nc-card.is-selected')).toHaveCount(1);
    const assignRoles = page.getByRole('button', { name: /assign roles/i });
    await expect(assignRoles).toBeVisible({ timeout: 5_000 });
    await assignRoles.click();
    await expect(page.locator('label[for="bulk-roles"]')).toBeVisible({ timeout: 5_000 });
  });

  test('bulk mode in the table view still selects the whole page', async ({ page }) => {
    await page.waitForSelector('.nc-card', { timeout: 8_000 });
    await useTable(page);
    await page
      .getByRole('button', { name: /bulk select/i })
      .first()
      .click();
    // Select-all header checkbox appears in bulk mode, then the action buttons enable.
    await page.locator('thead .p-checkbox, thead p-checkbox').first().click();
    const assignRoles = page.getByRole('button', { name: /assign roles/i });
    await expect(assignRoles).toBeEnabled({ timeout: 5_000 });
  });
});
