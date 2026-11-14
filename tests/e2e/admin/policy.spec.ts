import { test, expect, type APIRequestContext } from '@playwright/test';
import { ADMIN, STAFF, loginAdmin } from '../shared/helpers';

/**
 * Company policy (Phase 7m): the working week is a company setting, not a constant.
 *
 * WHY this matters: a Mon-Sat company must be able to book Saturday leave and a Mon-Fri company must
 * not, and only people with policy.manage may change it. The policy is GLOBAL for the installation,
 * so these tests run in order, start from a known Mon-Fri week and ALWAYS put back what they found.
 */

const BASE = process.env['BASE_URL'] ?? 'http://localhost:8080';

interface PolicyDto {
  workingDays: number[];
  workdayHours: number;
  annualLeaveAdvanceDays: number;
  maxLeaveDays: number;
  carryOverMode: string;
  carryOverMaxHours: number;
  unpaidLeaveDivisor: number;
  version: number;
}

const login = (ctx: APIRequestContext, staffId: string, password: string) =>
  ctx.post('/api/auth/login', { data: { staffId, password } });

async function getPolicy(ctx: APIRequestContext): Promise<PolicyDto> {
  const res = await ctx.get('/api/company-policy');
  expect(res.ok(), 'GET /api/company-policy').toBeTruthy();
  return (await res.json()) as PolicyDto;
}

/** Save `values` over the current policy, using the version the server holds right now. */
async function setPolicy(ctx: APIRequestContext, values: Partial<PolicyDto>): Promise<void> {
  const current = await getPolicy(ctx);
  const res = await ctx.put('/api/company-policy', { data: { ...current, ...values } });
  expect(res.ok(), `PUT /api/company-policy (${res.status()}): ${await res.text()}`).toBeTruthy();
}

/** yyyy-MM-dd of a Saturday at least `daysAhead` days from now. Random so repeated runs never clash. */
function futureSaturday(daysAhead: number): string {
  let d = new Date(Date.now() + daysAhead * 86_400_000);
  while (d.getUTCDay() !== 6) d = new Date(d.getTime() + 86_400_000);
  return d.toISOString().slice(0, 10);
}

test.describe.serial('Company policy', () => {
  let original: PolicyDto;
  let adminApi: APIRequestContext;
  let employeeApi: APIRequestContext;

  test.beforeAll(async ({ playwright }) => {
    adminApi = await playwright.request.newContext({ baseURL: BASE });
    expect((await login(adminApi, ADMIN.staffId, ADMIN.password)).ok(), 'admin login').toBeTruthy();
    employeeApi = await playwright.request.newContext({ baseURL: BASE });
    expect(
      (await login(employeeApi, STAFF.staffId, STAFF.password)).ok(),
      'employee login',
    ).toBeTruthy();

    original = await getPolicy(adminApi);
    // A known starting point: Monday to Friday, 8 hours.
    await setPolicy(adminApi, { workingDays: [1, 2, 3, 4, 5], workdayHours: 8 });
  });

  test.afterAll(async () => {
    // The policy is global: put back exactly what was there, whatever happened above.
    await setPolicy(adminApi, {
      workingDays: original.workingDays,
      workdayHours: original.workdayHours,
      annualLeaveAdvanceDays: original.annualLeaveAdvanceDays,
      maxLeaveDays: original.maxLeaveDays,
      carryOverMode: original.carryOverMode,
      carryOverMaxHours: original.carryOverMaxHours,
      unpaidLeaveDivisor: original.unpaidLeaveDivisor,
    });
    await adminApi.dispose();
    await employeeApi.dispose();
  });

  test('every signed-in user can read the policy, but an employee cannot change it', async () => {
    const read = await employeeApi.get('/api/company-policy');
    expect(read.ok(), 'an employee may read the working week').toBeTruthy();
    expect((await read.json()).workingDays).toEqual([1, 2, 3, 4, 5]);

    const current = await getPolicy(employeeApi);
    const write = await employeeApi.put('/api/company-policy', {
      data: { ...current, workdayHours: 6 },
    });
    expect(write.status(), 'an employee needs policy.manage to save').toBe(403);
  });

  test('an invalid policy is refused with a clean 400', async () => {
    const current = await getPolicy(adminApi);
    const res = await adminApi.put('/api/company-policy', {
      data: { ...current, workingDays: [] },
    });
    expect(res.status()).toBe(400);
    expect((await res.json()).error).toMatch(/working day/i);
  });

  test('a save made from a stale copy is refused (409) and changes nothing', async () => {
    const current = await getPolicy(adminApi);
    const res = await adminApi.put('/api/company-policy', {
      data: { ...current, workdayHours: 7, version: current.version - 1 },
    });
    expect(res.status()).toBe(409);
    expect((await getPolicy(adminApi)).workdayHours).toBe(8);
  });

  test('the administrator switches the company to Mon-Sat on the page; a Saturday becomes valid leave, and stops again after switching back', async ({
    page,
  }) => {
    const saturday = futureSaturday(120 + Math.floor(Math.random() * 200));
    const body = {
      leaveType: 'unpaid',
      startDate: saturday,
      endDate: saturday,
      startTime: '09:00',
      endTime: '17:00',
      hours: 8,
      notes: 'policy e2e',
    };

    // Mon-Fri: a Saturday is not a working day, so the request is refused.
    const before = await employeeApi.post('/api/leave', { data: body });
    expect(before.status(), 'Saturday leave in a Mon-Fri company').toBeGreaterThanOrEqual(400);
    expect(before.status()).toBeLessThan(500);

    // Switch to Mon-Sat through the UI.
    await loginAdmin(page);
    await expect(page.locator('.sidebar .nav-label', { hasText: 'Company Policy' })).toBeVisible();
    await page.goto('/policy');
    await expect(page.getByRole('heading', { name: 'Company Policy' })).toBeVisible();
    const sat = page.getByRole('button', { name: 'Sat', exact: true });
    await expect(sat).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('.example')).toContainText('Saturday is not a working day');
    await sat.click();
    await expect(sat).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.example')).toContainText(
      'A request for a single Saturday costs 8 hours',
    );
    await page.getByRole('button', { name: 'Save policy' }).click();
    await expect(page.getByText('Company policy saved.')).toBeVisible();
    expect((await getPolicy(adminApi)).workingDays).toEqual([1, 2, 3, 4, 5, 6]);

    // Mon-Sat: the same Saturday request is accepted and costs a full working day.
    let id = '';
    try {
      const during = await employeeApi.post('/api/leave', { data: body });
      expect(during.ok(), `Saturday leave in a Mon-Sat company (${during.status()})`).toBeTruthy();
      id = (await during.json()).id;
      const mine = (await (await employeeApi.get('/api/leave/mine')).json()) as {
        id: string;
        hours: number;
      }[];
      expect(mine.find((l) => l.id === id)?.hours, 'a Saturday costs one working day').toBe(8);
    } finally {
      if (id) await employeeApi.delete(`/api/leave/${id}`);
      // Back to Mon-Fri.
      await setPolicy(adminApi, { workingDays: [1, 2, 3, 4, 5] });
    }

    // Mon-Fri again: refused again.
    const after = await employeeApi.post('/api/leave', { data: body });
    expect(after.status(), 'Saturday leave after switching back').toBeGreaterThanOrEqual(400);
    expect(after.status()).toBeLessThan(500);
  });
});
