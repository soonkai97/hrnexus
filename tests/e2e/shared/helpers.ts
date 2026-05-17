import { Page } from '@playwright/test';

/**
 * Default test credentials — can be overridden via environment variables.
 * These match the seed_demo.sql accounts seeded by `npm run seed` / Docker init.
 *
 *   NC1002 = Alex (manager)  — has admin-app access
 *   NC1001 = Marcus (employee) — has staff-app access
 */
export const TEST_ADMIN_ID  = process.env['E2E_ADMIN_ID']       ?? 'NC1002';
export const TEST_ADMIN_PWD = process.env['E2E_ADMIN_PASSWORD']  ?? 'password123';
export const TEST_STAFF_ID  = process.env['E2E_STAFF_ID']        ?? 'NC1001';
export const TEST_STAFF_PWD = process.env['E2E_STAFF_PASSWORD']  ?? 'password123';

/** Logs in to the admin app with given credentials. */
export async function loginAdmin(
  page: Page,
  staffId  = TEST_ADMIN_ID,
  password = TEST_ADMIN_PWD,
): Promise<void> {
  await page.goto('/auth/login');

  // Click first to dismiss the readonly anti-autofill attribute.
  // Use exact: true on Password to avoid matching the "Show password" eye-button aria-label.
  const idInput  = page.getByLabel(/staff id/i);
  const pwdInput = page.getByLabel('Password', { exact: true });
  await idInput.click();
  await idInput.fill(staffId);
  await pwdInput.click();
  await pwdInput.fill(password);

  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL(url => !url.pathname.includes('/auth/login'), { timeout: 10_000 });
}

/** Logs in to the staff app. */
export async function loginStaff(
  page: Page,
  staffId  = TEST_STAFF_ID,
  password = TEST_STAFF_PWD,
): Promise<void> {
  await page.goto('/auth/login');

  const idInput  = page.getByLabel(/staff id/i);
  const pwdInput = page.getByLabel('Password', { exact: true });
  await idInput.click();
  await idInput.fill(staffId);
  await pwdInput.click();
  await pwdInput.fill(password);

  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL(url => !url.pathname.includes('/auth/login'), { timeout: 10_000 });
}
