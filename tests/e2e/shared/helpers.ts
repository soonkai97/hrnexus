import { Page } from '@playwright/test';

/** Logs in to the admin app with given credentials. */
export async function loginAdmin(page: Page, staffId: string, password: string): Promise<void> {
  await page.goto('/auth/login');
  await page.getByLabel(/staff id/i).fill(staffId);
  await page.getByLabel(/password/i).fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  // Wait for redirect away from login
  await page.waitForURL(url => !url.pathname.includes('/auth/login'), { timeout: 10_000 });
}

/** Logs in to the staff app. */
export async function loginStaff(page: Page, staffId: string, password: string): Promise<void> {
  await page.goto('/auth/login');
  await page.getByLabel(/staff id/i).fill(staffId);
  await page.getByLabel(/password/i).fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL(url => !url.pathname.includes('/auth/login'), { timeout: 10_000 });
}
