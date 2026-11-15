import {
  test,
  expect,
  request as pwRequest,
  type APIRequestContext,
  type Page,
} from '@playwright/test';
import { ensureRole } from '../shared/api';
import { loginAdmin, loginAs } from '../shared/helpers';

/**
 * Payroll run screens (Phase 8c-6).
 *
 * Strategy: arrange through the API, act and assert through the screens. Everything here is left clean: the run is
 * DELETED at the end (only a draft that was never reviewed can be), the pay item and cash advance are removed, and the people
 * are employed in one old year only, so they can never appear in a real payroll run. Submitting, returning and approving are NOT
 * driven here: a run that was returned or reopened cannot be deleted, and it would sit in the shared database as an open run that
 * blocks the next real month. Those steps are covered by the Application and Postgres HTTP tests.
 */

const API = 'http://localhost:8080';
const PASSWORD = 'Test@1234';
// A year of its own for each run of the suite (2001 to 2019), so people hired by earlier runs never pile into this month.
const RUN_YEAR = 2001 + (Date.now() % 19);
const RUN_MONTH = 6; // June

interface Person {
  id: string;
  staffId: string;
}

async function adminApi(): Promise<APIRequestContext> {
  const api = await pwRequest.newContext({ baseURL: API });
  const r = await api.post('/api/auth/login', {
    data: { staffId: 'HRADMIN', password: 'Admin@1234' },
  });
  expect(r.ok(), `admin login (${r.status()})`).toBeTruthy();
  return api;
}

/** Hires someone employed (and paid) in the suite's old year only. */
async function hire(
  api: APIRequestContext,
  prefix: string,
  roleId?: string,
  reportsTo?: string,
): Promise<Person> {
  const staffId = `${prefix}${Date.now().toString().slice(-7)}`;
  const r = await api.post('/api/staff', {
    data: {
      staffId,
      firstName: 'Pay',
      lastName: staffId,
      email: null,
      password: PASSWORD,
      roles: roleId ? [roleId] : [],
    },
  });
  expect(r.ok(), `hire ${staffId} (${r.status()})`).toBeTruthy();
  const id: string = (await r.json()).id;
  const emp = await api.put(`/api/staff/${id}/employment`, {
    data: {
      citizenshipStatus: 'citizen',
      startDate: `${RUN_YEAR}-01-01`,
      endDate: `${RUN_YEAR}-12-31`,
    },
  });
  expect(emp.ok(), `employment (${emp.status()})`).toBeTruthy();
  const sal = await api.post(`/api/staff/${id}/salary`, {
    data: { basicSalary: 3000, payFrequency: 'monthly', effectiveFrom: `${RUN_YEAR}-01-01` },
  });
  expect(sal.ok(), `salary (${sal.status()})`).toBeTruthy();
  if (reportsTo) {
    const rt = await api.put(`/api/staff/${id}/reports-to`, { data: { managerId: reportsTo } });
    expect(rt.ok(), `reports-to (${rt.status()})`).toBeTruthy();
  }
  return { id, staffId };
}

async function openRuns(
  api: APIRequestContext,
): Promise<Array<{ id: string; year: number; month: number; state: string }>> {
  const r = await api.get('/api/payroll/runs');
  expect(r.ok()).toBeTruthy();
  return (await r.json()) as Array<{ id: string; year: number; month: number; state: string }>;
}

async function deleteRunIfDraft(api: APIRequestContext): Promise<void> {
  for (const run of await openRuns(api)) {
    if (run.year === RUN_YEAR && run.month === RUN_MONTH && run.state === 'draft')
      await api.delete(`/api/payroll/runs/${run.id}`);
  }
}

async function chooseOption(page: Page, trigger: string, name: string): Promise<void> {
  await page.locator(trigger).click();
  await page.getByRole('option', { name }).click();
}

test.describe('Admin: payroll runs', () => {
  test('start a month, edit a person, recalculate, see it as cards, and delete the draft', async ({
    page,
  }) => {
    test.slow();
    const api = await adminApi();
    const open = (await openRuns(api)).filter(
      (r) => r.state !== 'locked' && !(r.year === RUN_YEAR && r.month === RUN_MONTH),
    );
    test.skip(
      open.length > 0,
      'another payroll run is open on this installation, so a new month cannot be started',
    );
    await deleteRunIfDraft(api); // a leftover from an interrupted run
    const person = await hire(api, 'PRUN');

    try {
      await loginAdmin(page);
      await page.goto('/payroll-runs');
      await expect(page.getByRole('heading', { name: 'Payroll Runs' })).toBeVisible({
        timeout: 8_000,
      });

      // Start June of the suite's year.
      await page.getByRole('button', { name: /start a month/i }).click();
      await chooseOption(page, '#pr-month', 'June');
      await page.locator('#pr-year').click();
      await page.locator('#pr-year').fill(String(RUN_YEAR));
      await page.getByRole('button', { name: 'Start', exact: true }).click();
      await page.waitForURL(/\/payroll-runs\/[0-9a-f-]{36}/, { timeout: 15_000 });

      await expect(page.getByRole('heading', { name: `June ${RUN_YEAR}` })).toBeVisible();
      await expect(page.getByText('Draft', { exact: true }).first()).toBeVisible();
      const row = page.locator('tbody tr', { hasText: person.staffId });
      await expect(row).toBeVisible({ timeout: 8_000 });
      await expect(row).toContainText('RM 3,000.00');

      // Open the person, enter a PCB, save.
      await row.getByRole('button', { name: 'Open' }).click();
      await expect(page.getByRole('dialog')).toContainText('Basic salary');
      await page.locator('#line-pcb input, input#line-pcb').first().click();
      await page.locator('#line-pcb input, input#line-pcb').first().fill('120');
      await page.getByRole('button', { name: 'Save', exact: true }).click();
      await expect(page.getByRole('dialog')).toHaveCount(0, { timeout: 8_000 });

      // Recalculate (a confirmation-free action) and see it as cards.
      await page.getByRole('button', { name: /recalculate/i }).click();
      await expect(page.getByText(/calculated again/i)).toBeVisible({ timeout: 8_000 });
      await page.getByRole('button', { name: 'Card view' }).click();
      await expect(page.locator('.nc-card', { hasText: person.staffId })).toBeVisible();
      await page.getByRole('button', { name: 'Table view' }).click();

      // Delete the draft (a never-reviewed draft can be deleted).
      await page
        .getByRole('button', { name: /delete/i })
        .first()
        .click();
      await page.locator('.p-confirmdialog').getByRole('button', { name: 'Delete' }).click();
      await page.waitForURL(/\/payroll-runs$/, { timeout: 10_000 });
      await expect(page.locator('tbody tr', { hasText: `June ${RUN_YEAR}` })).toHaveCount(0);
    } finally {
      await deleteRunIfDraft(api);
      await api.dispose();
    }
  });

  test('a second month cannot be started while one is open, and the reason is shown', async ({
    page,
  }) => {
    test.slow();
    const api = await adminApi();
    const open = (await openRuns(api)).filter(
      (r) => r.state !== 'locked' && !(r.year === RUN_YEAR && r.month === RUN_MONTH),
    );
    test.skip(open.length > 0, 'another payroll run is open on this installation');
    await deleteRunIfDraft(api);
    await hire(api, 'PRUN');
    const first = await api.post('/api/payroll/runs', {
      data: { year: RUN_YEAR, month: RUN_MONTH },
    });
    expect(first.ok(), `start (${first.status()})`).toBeTruthy();

    try {
      await loginAdmin(page);
      await page.goto('/payroll-runs');
      await page.getByRole('button', { name: /start a month/i }).click();
      await chooseOption(page, '#pr-month', 'July');
      await page.locator('#pr-year').click();
      await page.locator('#pr-year').fill(String(RUN_YEAR));
      await page.getByRole('button', { name: 'Start', exact: true }).click();

      await expect(page.getByText(/not locked yet/i)).toBeVisible({ timeout: 8_000 });
      await expect(page.getByRole('dialog')).toBeVisible(); // the dialog stays open
    } finally {
      await deleteRunIfDraft(api);
      await api.dispose();
    }
  });
});

test.describe('Admin: pay items, reports to and cash advance schedule', () => {
  test('add and delete an allowance on the staff Pay tab', async ({ page }) => {
    test.slow();
    const api = await adminApi();
    const person = await hire(api, 'PITM');
    try {
      await loginAdmin(page);
      await page.goto(`/staff/${person.id}`);
      await page.getByRole('tab', { name: 'Pay' }).click();
      await expect(page.getByRole('heading', { name: 'Allowances and deductions' })).toBeVisible({
        timeout: 8_000,
      });

      await page.locator('app-pay-items').getByRole('button', { name: /Add$/ }).click();
      await page.locator('#pi-reason').click();
      await page.locator('#pi-reason').fill('E2E transport');
      await page.locator('#pi-amount').click();
      await page.locator('#pi-amount').fill('150');
      await page.keyboard.press('Tab'); // the amount box commits its value when it loses focus
      await page.getByRole('dialog').getByRole('button', { name: 'Add', exact: true }).click();

      await expect(page.getByText('E2E transport')).toBeVisible({ timeout: 8_000 });
      await expect(page.getByText('RM 150.00 a month')).toBeVisible();

      await page
        .getByRole('button', { name: /delete/i })
        .first()
        .click();
      await page.locator('.p-confirmdialog').getByRole('button', { name: 'Delete' }).click();
      await expect(page.getByText('E2E transport')).toHaveCount(0, { timeout: 8_000 });
    } finally {
      await api.dispose();
    }
  });

  test('set who a person reports to and clear it', async ({ page }) => {
    test.slow();
    const api = await adminApi();
    const boss = await hire(api, 'PBOS');
    const worker = await hire(api, 'PWRK');
    try {
      await loginAdmin(page);
      await page.goto(`/staff/${worker.id}`);
      await expect(page.getByRole('heading', { name: 'Reports to' })).toBeVisible({
        timeout: 8_000,
      });

      await page
        .locator('p-select[ariaLabel="Reports to"], p-select[aria-label="Reports to"]')
        .first()
        .click();
      await page.getByPlaceholder('Search by name or Staff ID').fill(boss.staffId);
      await page.getByRole('option', { name: new RegExp(boss.staffId) }).click();
      await page.getByRole('button', { name: 'Save', exact: true }).last().click();
      await expect(page.getByText('Saved.')).toBeVisible({ timeout: 8_000 });

      const detail = await (await api.get(`/api/staff/${worker.id}`)).json();
      expect(detail.reportsToId).toBe(boss.id);

      const cleared = await api.put(`/api/staff/${worker.id}/reports-to`, {
        data: { managerId: null },
      });
      expect(cleared.ok()).toBeTruthy();
    } finally {
      await api.dispose();
    }
  });

  test('schedule the monthly repayment of a cash advance', async ({ page }) => {
    test.slow();
    const api = await adminApi();
    const person = await hire(api, 'PCSH');
    const created = await api.post('/api/cash-advances', {
      data: {
        employeeId: person.id,
        amount: 500,
        advanceDate: new Date().toISOString().slice(0, 10),
        reason: 'e2e',
      },
    });
    expect(created.ok(), `advance (${created.status()})`).toBeTruthy();
    const advanceId: string = (await created.json()).id;
    try {
      await loginAdmin(page);
      await page.goto('/cash-advances');
      const row = page.locator('tbody tr', { hasText: person.staffId });
      await expect(row).toBeVisible({ timeout: 8_000 });
      await expect(row).toContainText('All in the next run');

      await row.getByRole('button', { name: 'Schedule' }).click();
      await page.locator('#ca-schedule').click();
      await page.locator('#ca-schedule').fill('100');
      await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();

      await expect(row).toContainText('RM 100.00 a month', { timeout: 8_000 });
      await expect(row).toContainText('RM 500.00 left');
    } finally {
      await api.delete(`/api/cash-advances/${advanceId}`);
      await api.dispose();
    }
  });
});

test.describe('Access: who sees the payroll screens', () => {
  test("a manager sees My team's pay and nothing of the payroll lists", async ({ page }) => {
    const api = await adminApi();
    const roleName = `PRMGR${Date.now().toString().slice(-6)}`;
    const roleId = await ensureRole(api, roleName);
    const perms = await api.put(`/api/permissions/roles/${roleName}`, {
      data: { permissions: ['payroll.signoff'] },
    });
    expect(perms.ok(), `role permissions (${perms.status()})`).toBeTruthy();
    const manager = await hire(api, 'PMGR', roleId);
    try {
      await loginAs(page, manager.staffId, PASSWORD);
      await page.waitForURL(/\/home/, { timeout: 15_000 });

      await expect(page.getByRole('link', { name: /my team's pay/i })).toBeVisible({
        timeout: 8_000,
      });
      await expect(page.getByRole('link', { name: 'Payroll Runs' })).toHaveCount(0);

      await page.getByRole('link', { name: /my team's pay/i }).click();
      await expect(page.getByRole('heading', { name: "My team's pay" })).toBeVisible();
      await expect(page.getByText(/nothing is waiting for you/i)).toBeVisible({ timeout: 8_000 });

      await page.goto('/payroll-runs');
      await page.waitForURL(/\/home/, { timeout: 8_000 }); // no payroll.view: back to home
    } finally {
      await api.dispose();
    }
  });

  test('the payroll endpoints answer 403 to someone with only the manager permission', async () => {
    const api = await adminApi();
    const roleName = `PRMG2${Date.now().toString().slice(-6)}`;
    const roleId = await ensureRole(api, roleName);
    await api.put(`/api/permissions/roles/${roleName}`, {
      data: { permissions: ['payroll.signoff'] },
    });
    const manager = await hire(api, 'PMG2', roleId);
    const mgr = await pwRequest.newContext({ baseURL: API });
    const login = await mgr.post('/api/auth/login', {
      data: { staffId: manager.staffId, password: PASSWORD },
    });
    expect(login.ok()).toBeTruthy();
    try {
      expect((await mgr.get('/api/payroll/runs')).status()).toBe(403);
      expect(
        (
          await mgr.post('/api/payroll/runs', { data: { year: RUN_YEAR, month: RUN_MONTH } })
        ).status(),
      ).toBe(403);
      expect((await mgr.get('/api/payroll/runs/team')).status()).toBe(200);
    } finally {
      await mgr.dispose();
      await api.dispose();
    }
  });
});
