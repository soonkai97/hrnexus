import { test, expect, request as pwRequest } from '@playwright/test';
import { loginAdmin } from '../shared/helpers';

/**
 * FULL BUSINESS WORKFLOW — the end-to-end "money path", in one chained test.
 *
 *   hire a staff (role + location)  →  staff logs a completed shift
 *   →  staff applies for leave  →  admin approves it
 *   →  it all surfaces correctly in BOTH portals + the report.
 *
 * Strategy: **API-arrange + UI-assert.** Each mutation is driven through the real
 * backend endpoint (so the whole command chain + read models are exercised), then the
 * *result* is verified through the actual UI on both the staff (8081) and admin (8080)
 * portals. We deliberately do NOT script PrimeNG date/time/multiselect pickers — that
 * makes workflow tests flaky, and a flaky end-to-end test is worse than none.
 *
 * Admin and staff API calls use SEPARATE request contexts (separate cookie jars) so the
 * shared-host auth cookie of one portal never clobbers the other.
 */

const ADMIN = { staffId: 'HRADMIN', password: 'Admin@1234' };
const API   = 'http://localhost:8080';   // both portals proxy /api to the same backend
const STAFF_WEB = 'http://localhost:8081';

const isoDate = (d: Date) => d.toISOString().slice(0, 10);

test.describe('Full workflow — hire → shift → leave → approve → report', () => {
  test('the end-to-end money path holds across both portals', async ({ browser }) => {
    test.slow();   // long chain — give it 3× the timeout

    const uid       = Date.now().toString().slice(-7);
    const staffId   = `WF${uid}`;
    const roleName  = `WFROLE${uid}`.toUpperCase();   // backend stores roles uppercase
    const password  = 'Test@1234';
    const workDate  = isoDate(new Date());                              // today → inside the report's default range
    const leaveDay  = isoDate(new Date(Date.now() + 21 * 86_400_000));  // 3 weeks out (clears advance rules; unpaid anyway)

    // ── Separate API cookie jars ────────────────────────────────────────────────
    const admin = await pwRequest.newContext({ baseURL: API });
    const staff = await pwRequest.newContext({ baseURL: API });

    // ── 1. Admin signs in ───────────────────────────────────────────────────────
    let r = await admin.post('/api/auth/login', { data: { staffId: ADMIN.staffId, password: ADMIN.password, portal: 'admin' } });
    expect(r.ok(), `admin login (${r.status()})`).toBeTruthy();

    // ── 2. Pick a venue, create a paid role, hire the staff onto both ───────────
    r = await admin.get('/api/locations');
    const locations = await r.json();
    expect(locations.length, 'seed has at least one location').toBeGreaterThan(0);
    const locationId: string = locations[0].id;

    r = await admin.post('/api/staff/roles', { data: { name: roleName, hourlyRate: 25 } });
    expect(r.ok(), `create role (${r.status()})`).toBeTruthy();

    r = await admin.post('/api/staff', { data: {
      staffId, firstName: 'Flow', lastName: 'Test', email: null, password,
      roles: [roleName], locations: [locationId],
    } });
    expect(r.ok(), `hire staff (${r.status()})`).toBeTruthy();
    const employeeId: string = (await r.json()).id;
    expect(employeeId).toBeTruthy();

    // ── 3. Staff signs in and logs a completed 09:00–17:00 shift ────────────────
    r = await staff.post('/api/auth/login', { data: { staffId, password, portal: 'staff' } });
    expect(r.ok(), `staff login (${r.status()})`).toBeTruthy();
    const staffUser = (await r.json()).user;

    r = await staff.post('/api/worklogs/entry', { data: {
      roleName, locationId, workDate, startTime: '09:00', endTime: '17:00', notes: 'e2e-workflow',
    } });
    expect(r.ok(), `log shift (${r.status()})`).toBeTruthy();

    // ── 4. Staff applies for leave (unpaid → no balance/advance constraints) ────
    r = await staff.post('/api/leave', { data: {
      leaveType: 'unpaid', startDate: leaveDay, endDate: leaveDay,
      startTime: '09:00', endTime: '17:00', hours: 8, notes: 'e2e-workflow',
    } });
    expect(r.ok(), `apply leave (${r.status()})`).toBeTruthy();

    // ── 5. Admin finds the pending request and approves it ──────────────────────
    r = await admin.get('/api/admin/leave?status=pending&page=1&pageSize=200');
    const pendingBody = await r.json();
    const pending = (pendingBody.items ?? pendingBody) as Array<{ id: string; employeeId?: string }>;
    const mine = pending.find(l => l.employeeId === employeeId);
    expect(mine, 'the new staff has a pending leave row').toBeTruthy();

    r = await admin.put(`/api/admin/leave/${mine!.id}`, { data: {
      employeeId, leaveType: 'unpaid', startDate: leaveDay, endDate: leaveDay,
      startTime: '09:00', endTime: '17:00', hours: 8, notes: 'e2e-workflow', status: 'approved',
    } });
    expect(r.ok(), `approve leave (${r.status()})`).toBeTruthy();

    // ── 6. API-level chain checks (read models reflect the chain) ───────────────
    r = await staff.get('/api/worklogs/my?page=1&pageSize=50');
    const myLogsBody = await r.json();
    const myLogs = (myLogsBody.items ?? myLogsBody) as Array<{ workDate: string; hours: number }>;
    expect(myLogs.some(w => w.workDate === workDate), 'shift is in the staff work-log read model').toBeTruthy();

    r = await admin.get('/api/admin/leave?status=approved&page=1&pageSize=200');
    const approvedBody = await r.json();
    const approved = (approvedBody.items ?? approvedBody) as Array<{ employeeId?: string }>;
    expect(approved.some(l => l.employeeId === employeeId), 'leave now shows approved').toBeTruthy();

    // ── 7. UI proof #1 — STAFF portal shows the completed shift ─────────────────
    const staffCtx = await browser.newContext();
    await staffCtx.addCookies((await staff.storageState()).cookies);
    const staffPage = await staffCtx.newPage();
    // Seed the SPA's display user (per-origin localStorage), then open Log Shift → History.
    await staffPage.goto(`${STAFF_WEB}/auth/login`);
    await staffPage.evaluate(u => localStorage.setItem('auth_user', JSON.stringify(u)), staffUser);
    await staffPage.goto(`${STAFF_WEB}/log-shift`);
    await staffPage.getByRole('tab', { name: /history/i }).click();
    // The just-logged shift renders as a history card (role uppercased, with the times).
    await expect(staffPage.locator('.history-card').first()).toBeVisible({ timeout: 10_000 });
    await expect(staffPage.locator('.history-card').filter({ hasText: roleName }).first())
      .toBeVisible({ timeout: 8_000 });
    await staffCtx.close();

    // ── 8. UI proof #2 — ADMIN report includes the shift (hours × rate) ─────────
    const adminPage = await browser.newPage();
    await loginAdmin(adminPage);
    await adminPage.goto('/reports');
    await adminPage.waitForSelector('.filter-bar', { timeout: 8_000 });
    await adminPage.getByRole('button', { name: /run.*report|generate/i }).click();
    await adminPage.waitForSelector('.stat-card', { timeout: 15_000 });
    // Open the All-Shifts tab and confirm our staff's shift is in the table.
    await adminPage.getByRole('tab', { name: /all.?shift|shift detail/i }).first().click();
    await expect(adminPage.locator('table').filter({ hasText: staffId }).first())
      .toBeVisible({ timeout: 8_000 });
    await adminPage.close();

    await admin.dispose();
    await staff.dispose();
  });
});
