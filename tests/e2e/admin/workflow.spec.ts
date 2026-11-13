import { test, expect, request as pwRequest } from '@playwright/test';
import { ensureRole } from '../shared/api';

/**
 * FULL BUSINESS WORKFLOW — the end-to-end path, in one chained test.
 *
 *   hire a staff (with a role)  →  staff applies for leave
 *   →  admin approves it  →  the approval shows in the read models.
 *
 * Strategy: **API-arrange + API-assert.** Each mutation is driven through the real
 * backend endpoint (so the whole command chain + read models are exercised). We deliberately
 * do NOT script PrimeNG date/time/multiselect pickers — that makes workflow tests flaky, and
 * a flaky end-to-end test is worse than none.
 *
 * Admin and staff API calls use SEPARATE request contexts (separate cookie jars) so the
 * shared-host auth cookie of one portal never clobbers the other.
 */

const ADMIN = { staffId: 'HRADMIN', password: 'Admin@1234' };
const API   = 'http://localhost:8080';   // both portals proxy /api to the same backend

const isoDate = (d: Date) => d.toISOString().slice(0, 10);

test.describe('Full workflow — hire → leave → approve', () => {
  test('the end-to-end path holds across both portals', async () => {
    test.slow();   // long chain — give it 3× the timeout

    const uid       = Date.now().toString().slice(-7);
    const staffId   = `WF${uid}`;
    const roleName  = `WFROLE${uid}`.toUpperCase();   // backend stores roles uppercase
    const password  = 'Test@1234';
    const leaveDay  = isoDate(new Date(Date.now() + 21 * 86_400_000));  // 3 weeks out (clears advance rules; unpaid anyway)

    // ── Separate API cookie jars ────────────────────────────────────────────────
    const admin = await pwRequest.newContext({ baseURL: API });
    const staff = await pwRequest.newContext({ baseURL: API });

    // ── 1. Admin signs in ───────────────────────────────────────────────────────
    let r = await admin.post('/api/auth/login', { data: { staffId: ADMIN.staffId, password: ADMIN.password, portal: 'admin' } });
    expect(r.ok(), `admin login (${r.status()})`).toBeTruthy();

    // ── 2. Create a role, hire the staff onto it ────────────────────────────────
    const roleId = await ensureRole(admin, roleName, 25);

    r = await admin.post('/api/staff', { data: {
      staffId, firstName: 'Flow', lastName: 'Test', email: null, password,
      roles: [roleId],
    } });
    expect(r.ok(), `hire staff (${r.status()})`).toBeTruthy();
    const employeeId: string = (await r.json()).id;
    expect(employeeId).toBeTruthy();

    // ── 3. Staff signs in ───────────────────────────────────────────────────────
    r = await staff.post('/api/auth/login', { data: { staffId, password, portal: 'staff' } });
    expect(r.ok(), `staff login (${r.status()})`).toBeTruthy();

    // ── 4. Staff applies for leave (unpaid → no balance/advance constraints) ────
    r = await staff.post('/api/leave', { data: {
      leaveType: 'unpaid', startDate: leaveDay, endDate: leaveDay,
      startTime: '09:00', endTime: '17:00', hours: 8, notes: 'e2e-workflow',
    } });
    expect(r.ok(), `apply leave (${r.status()})`).toBeTruthy();

    // ── 5. Admin finds the pending request and approves it ──────────────────────
    r = await admin.get(`/api/admin/leave?status=pending&employeeId=${employeeId}&page=1&pageSize=50`);
    const pendingBody = await r.json();
    const pending = (pendingBody.items ?? pendingBody) as Array<{ id: string; employeeId?: string }>;
    const mine = pending.find(l => l.employeeId === employeeId);
    expect(mine, 'the new staff has a pending leave row').toBeTruthy();

    r = await admin.put(`/api/admin/leave/${mine!.id}`, { data: {
      employeeId, leaveType: 'unpaid', startDate: leaveDay, endDate: leaveDay,
      startTime: '09:00', endTime: '17:00', hours: 8, notes: 'e2e-workflow', status: 'approved',
    } });
    expect(r.ok(), `approve leave (${r.status()})`).toBeTruthy();

    // ── 6. Read models reflect the chain ────────────────────────────────────────
    r = await admin.get(`/api/admin/leave?status=approved&employeeId=${employeeId}&page=1&pageSize=50`);
    const approvedBody = await r.json();
    const approved = (approvedBody.items ?? approvedBody) as Array<{ employeeId?: string }>;
    expect(approved.some(l => l.employeeId === employeeId), 'leave now shows approved').toBeTruthy();

    await admin.dispose();
    await staff.dispose();
  });
});
