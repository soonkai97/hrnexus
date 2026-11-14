import { test, expect, type APIRequestContext } from '@playwright/test';
import { ADMIN, STAFF, loginAdmin } from '../shared/helpers';

/**
 * Leave types as company data (Phase 7q).
 *
 * WHY this matters: the company decides which kinds of leave exist and how they behave. Staff must be able to
 * take the owner's types (a 98-day maternity is not blocked by the 30-day cap), must never be offered a type
 * only an administrator records, and an administrator must be able to add and switch off a type from the
 * policy page. The catalogue is GLOBAL for the installation, so these tests run in order and ALWAYS put back
 * what they found.
 */

const BASE = process.env['BASE_URL'] ?? 'http://localhost:8080';

interface TypeDto {
  key: string;
  [field: string]: unknown;
}
interface CatalogueDto {
  version: number;
  isSaved: boolean;
  types: TypeDto[];
}

const login = (ctx: APIRequestContext, staffId: string, password: string) =>
  ctx.post('/api/auth/login', { data: { staffId, password } });

async function getAll(ctx: APIRequestContext): Promise<CatalogueDto> {
  const res = await ctx.get('/api/leave-types/all');
  expect(res.ok(), 'GET /api/leave-types/all').toBeTruthy();
  return (await res.json()) as CatalogueDto;
}

/** Save `types` using the version the server holds right now. */
async function saveTypes(ctx: APIRequestContext, types: TypeDto[]): Promise<void> {
  const current = await getAll(ctx);
  const res = await ctx.put('/api/leave-types', { data: { version: current.version, types } });
  expect(res.ok(), `PUT /api/leave-types (${res.status()}): ${await res.text()}`).toBeTruthy();
}

/** yyyy-MM-dd of the first Monday at least `daysAhead` days from now. */
function futureMonday(daysAhead: number): string {
  let d = new Date(Date.now() + daysAhead * 86_400_000);
  while (d.getUTCDay() !== 1) d = new Date(d.getTime() + 86_400_000);
  return d.toISOString().slice(0, 10);
}

const addDays = (iso: string, days: number): string =>
  new Date(Date.parse(iso) + days * 86_400_000).toISOString().slice(0, 10);

test.describe.serial('Leave types', () => {
  let original: CatalogueDto;
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
    original = await getAll(adminApi);
  });

  test.afterAll(async () => {
    // The catalogue is global: put back the types that were there. A type a test added is removed; one that
    // somehow got leave cannot be (history is protected), so it is left switched off instead.
    const now = await getAll(adminApi);
    const originalKeys = new Set(original.types.map((t) => t.key));
    const extra = now.types.filter((t) => !originalKeys.has(t.key));
    const keepOff = extra
      .filter((t) => t['hasLeave'] === true)
      .map((t) => ({ ...t, active: false }));
    await saveTypes(adminApi, [...original.types, ...keepOff]);
    await adminApi.dispose();
    await employeeApi.dispose();
  });

  test('every signed-in user reads the types, but an employee cannot change them or see usage', async () => {
    const read = await employeeApi.get('/api/leave-types');
    expect(read.ok(), 'an employee reads the catalogue').toBeTruthy();
    const keys = ((await read.json()) as CatalogueDto).types.map((t) => t.key);
    for (const key of [
      'annual',
      'sick',
      'hospitalisation',
      'maternity',
      'paternity',
      'compassionate',
    ])
      expect(keys, `the owner's type '${key}'`).toContain(key);

    expect((await employeeApi.get('/api/leave-types/all')).status()).toBe(403);
    const write = await employeeApi.put('/api/leave-types', { data: { version: 0, types: [] } });
    expect(write.status(), 'an employee needs policy.manage to save').toBe(403);
  });

  test('an employee cannot record an admin-only type, but a 98-day maternity is accepted and 99 is refused', async () => {
    const start = futureMonday(60 + Math.floor(Math.random() * 120));

    const noShow = await employeeApi.post('/api/leave', {
      data: { leaveType: 'no-show', startDate: start, endDate: start },
    });
    expect(noShow.status(), 'no-show is recorded by an administrator').toBe(403);

    const tooLong = await employeeApi.post('/api/leave', {
      data: { leaveType: 'maternity', startDate: start, endDate: addDays(start, 98) },
    });
    expect(tooLong.status(), '99 calendar days is over the maternity limit').toBe(400);
    expect(((await tooLong.json()) as { error: string }).error).toMatch(/98/);

    const ok = await employeeApi.post('/api/leave', {
      data: { leaveType: 'maternity', startDate: start, endDate: addDays(start, 97) },
    });
    expect(ok.ok(), `98 calendar days (${ok.status()}): ${await ok.text()}`).toBeTruthy();
    const { id } = (await ok.json()) as { id: string };

    try {
      const mine = (await (await employeeApi.get('/api/leave/mine')).json()) as {
        id: string;
        hours: number;
        status: string;
      }[];
      const leave = mine.find((l) => l.id === id)!;
      expect(leave.hours, '98 days x 8 hours, weekends included').toBe(784);
      expect(leave.status, 'a manager decides a long absence').toBe('pending');
    } finally {
      // Withdraw it even if an assertion failed, so a repeated run never overlaps.
      await employeeApi.delete(`/api/leave/${id}`);
    }
  });

  test('the administrator adds a type on the policy page and staff are offered it; switching it off stops new requests', async ({
    page,
  }) => {
    const key = `study${Math.floor(Math.random() * 90_000) + 10_000}`.slice(0, 12);
    const label = `Exam ${key}`;

    await loginAdmin(page);
    await page.goto('/policy');
    await expect(page.getByRole('heading', { name: 'Leave types' })).toBeVisible();
    await expect(page.locator('.type-row', { hasText: 'Maternity' })).toBeVisible();

    // Add a type through the dialog and save: the Save button settles once the server has it.
    const save = page.getByRole('button', { name: 'Save leave types' });
    await page.getByRole('button', { name: 'Add type' }).click();
    // Inputs start readonly (anti-autofill): a click makes them editable before typing.
    await page.locator('#lt-label').click();
    await page.locator('#lt-label').fill(label);
    await page.locator('#lt-key').click();
    await page.locator('#lt-key').fill(key);
    await page.getByRole('button', { name: 'Done' }).click();
    await expect(page.locator('.type-row', { hasText: label })).toBeVisible();
    await save.click();
    await expect(save).toBeDisabled();

    // The catalogue is the only source of what staff are offered (poll: the save is still in flight).
    await expect
      .poll(
        async () =>
          ((await (await employeeApi.get('/api/leave-types')).json()) as CatalogueDto).types.map(
            (t) => t.key,
          ),
        { timeout: 10_000 },
      )
      .toContain(key);

    // Switch it off on the page: it stops taking new requests (and no leave row is created).
    await page
      .locator('.type-row', { hasText: label })
      .getByRole('button', { name: 'Turn off' })
      .click();
    await save.click();
    await expect(page.locator('.type-row.inactive', { hasText: label })).toBeVisible();
    await expect
      .poll(
        async () =>
          (
            (await getAll(adminApi)).types.find((t) => t.key === key) as
              { active?: boolean } | undefined
          )?.active,
        { timeout: 10_000 },
      )
      .toBe(false);
    const day = futureMonday(200 + Math.floor(Math.random() * 100));
    const off = await employeeApi.post('/api/leave', {
      data: { leaveType: key, startDate: day, endDate: day },
    });
    expect(off.status(), 'a deactivated type takes no new requests').toBe(400);
  });

  test('a stale save is refused (409), a missing system type is a clean 400, and nothing changes', async () => {
    const current = await getAll(adminApi);

    const stale = await adminApi.put('/api/leave-types', {
      data: { version: current.version - 1, types: current.types },
    });
    expect(stale.status()).toBe(409);

    const withoutSick = await adminApi.put('/api/leave-types', {
      data: { version: current.version, types: current.types.filter((t) => t.key !== 'sick') },
    });
    expect(withoutSick.status()).toBe(400);
    expect(((await withoutSick.json()) as { error: string }).error).toMatch(/system type/i);

    expect((await getAll(adminApi)).version, 'neither refused save changed anything').toBe(
      current.version,
    );
  });
});
