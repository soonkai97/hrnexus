import { test, expect, request as pwRequest, type APIRequestContext } from '@playwright/test';
import { ensureRole, futureWeekday } from '../shared/api';

/**
 * NEGATIVE PATHS — every business guard must reject cleanly through the full stack.
 *
 * These drive the real running system end-to-end (nginx → API → middleware → command →
 * guard) and assert a clean 4xx + message, NOT a 5xx crash. Asserting at the HTTP layer
 * (rather than scripting the UI for each failure) keeps them fast and non-flaky while still
 * exercising the entire request pipeline. Complements the happy-path workflow.spec and the
 * 300+ backend unit/integration negative tests.
 *
 * Serial: shared fixtures (a venue the staff is assigned to, a role, a
 * staff) are created once. Guards that "block a delete" don't actually delete, so state holds.
 */

const ADMIN = { staffId: 'HRADMIN', password: 'Admin@1234' };
const API   = 'http://localhost:8080';
const isoDate = (d: Date) => d.toISOString().slice(0, 10);

/** A rejection must be a deliberate 4xx (client error), never a 5xx (server crash). */
const expectRejected = (status: number, what: string) => {
  expect(status, `${what} → expected 4xx, got ${status}`).toBeGreaterThanOrEqual(400);
  expect(status, `${what} → should be a clean 4xx, not a 5xx crash`).toBeLessThan(500);
};

test.describe.serial('Negative paths — guards reject cleanly (full stack)', () => {
  let admin: APIRequestContext;
  let staff: APIRequestContext;
  let roleName    = '';
  let roleId      = '';
  let staffId     = '';

  test.beforeAll(async () => {
    const uid = Date.now().toString().slice(-7);
    staffId  = `NEG${uid}`;
    roleName = `NEGROLE${uid}`.toUpperCase();
    const password = 'Test@1234';

    admin = await pwRequest.newContext({ baseURL: API });
    let r = await admin.post('/api/auth/login', { data: { staffId: ADMIN.staffId, password: ADMIN.password } });
    expect(r.ok(), 'admin login').toBeTruthy();

    roleId = await ensureRole(admin, roleName);
    r = await admin.post('/api/staff', { data: {
      staffId, firstName: 'Neg', lastName: 'Case', email: null, password,
      roles: [roleId],
    } });
    expect(r.ok(), 'hire staff').toBeTruthy();

    staff = await pwRequest.newContext({ baseURL: API });
    r = await staff.post('/api/auth/login', { data: { staffId, password } });
    expect(r.ok(), 'staff login').toBeTruthy();
  });

  test.afterAll(async () => { await admin?.dispose(); await staff?.dispose(); });

  // ── Leave overlap guard ─────────────────────────────────────────────────────
  test('overlapping leave request is rejected', async () => {
    const day = futureWeekday(30);
    const body = { leaveType: 'unpaid', startDate: day, endDate: day, startTime: '09:00', endTime: '17:00', hours: 8, notes: 'neg' };
    const ok = await staff.post('/api/leave', { data: body });
    expect(ok.ok(), 'first leave should be accepted').toBeTruthy();
    const clash = await staff.post('/api/leave', { data: body });
    expectRejected(clash.status(), 'overlapping leave');
  });

  // ── Phase 7l: cancelled leave frees its dates; nobody approves their own leave ──
  test('cancelling a leave frees its dates for a new request', async () => {
    const day = futureWeekday(45);
    const body = { leaveType: 'unpaid', startDate: day, endDate: day, startTime: '09:00', endTime: '17:00', hours: 8, notes: 'neg' };
    const first = await staff.post('/api/leave', { data: body });
    expect(first.ok(), 'first leave should be accepted').toBeTruthy();
    const { id } = await first.json();

    const cancel = await staff.delete(`/api/leave/${id}`);
    expect(cancel.status(), 'cancel the leave').toBe(204);

    const again = await staff.post('/api/leave', { data: body });
    expect(again.ok(), 'the same dates can be requested again after cancelling').toBeTruthy();
  });

  test('an approver cannot record their own leave as already approved', async () => {
    const me = await (await admin.get('/api/auth/me')).json();
    // HRADMIN persists between runs, so use a random date and remove the pending leave afterwards.
    const day = futureWeekday(60 + Math.floor(Math.random() * 120));
    const body = { employeeId: me.id, leaveType: 'unpaid', startDate: day, endDate: day, startTime: '09:00', endTime: '17:00', hours: 8, notes: 'neg' };

    const approved = await admin.post('/api/admin/leave', { data: { ...body, status: 'approved' } });
    expect(approved.status(), 'own leave recorded as approved').toBe(409);
    expect((await approved.json()).error, 'refused because it is their own leave').toMatch(/own leave/i);

    const pending = await admin.post('/api/admin/leave', { data: { ...body, status: 'pending' } });
    expect(pending.ok(), 'own leave recorded as pending is allowed').toBeTruthy();
    const { id } = await pending.json();
    await admin.delete(`/api/admin/leave/${id}`);
  });

  // ── Phase 8a: salary is invisible without a payroll permission and history is append-only ──
  test('salary is hidden from staff without payroll permission and history is append-only', async () => {
    const me = await (await staff.get('/api/auth/me')).json();

    const forbidden = await staff.get(`/api/staff/${me.id}/salary`);
    expect(forbidden.status(), 'a plain staff member cannot read salary').toBe(403);
    const forbiddenWrite = await staff.post(`/api/staff/${me.id}/salary`, {
      data: { basicSalary: 9999, payFrequency: 'monthly', effectiveFrom: '2026-01-01' },
    });
    expect(forbiddenWrite.status(), 'a plain staff member cannot set salary').toBe(403);

    const body = { basicSalary: 3500, payFrequency: 'monthly', effectiveFrom: '2026-01-01', bankName: 'Maybank', bankAccount: '1234567890', bankHolder: 'Neg Case' };
    expect((await admin.post(`/api/staff/${me.id}/salary`, { data: body })).status()).toBe(201);
    expect((await admin.post(`/api/staff/${me.id}/salary`, { data: { ...body, basicSalary: 4000 } })).status(), 'same date again').toBe(409);

    const seen = await (await admin.get(`/api/staff/${me.id}/salary`)).json();
    expect(seen.history).toHaveLength(1);
    expect(seen.history[0].basicSalary).toBe(3500);
  });

  // ── Leave field validation ──────────────────────────────────────────────────
  test('leave with start after end is rejected', async () => {
    const start = isoDate(new Date(Date.now() + 60 * 86_400_000));
    const end   = isoDate(new Date(Date.now() + 59 * 86_400_000));
    const r = await staff.post('/api/leave', {
      data: { leaveType: 'unpaid', startDate: start, endDate: end, startTime: '09:00', endTime: '17:00', hours: 8 },
    });
    expectRejected(r.status(), 'leave start-after-end');
  });

  test('leave exceeding the max hours cap is rejected', async () => {
    const start = isoDate(new Date(Date.now() + 90 * 86_400_000));
    const end   = isoDate(new Date(Date.now() + 200 * 86_400_000));
    const r = await staff.post('/api/leave', {
      data: { leaveType: 'unpaid', startDate: start, endDate: end, startTime: '09:00', endTime: '17:00', hours: 9999 },
    });
    expectRejected(r.status(), 'leave over the hours cap');
  });

  // ── Delete-in-use guards (must block, must NOT delete) ──────────────────────
  test('deleting a role still assigned to staff is blocked', async () => {
    const r = await admin.delete(`/api/staff/roles/${roleId}`);
    expectRejected(r.status(), 'delete role assigned to staff');
  });

  // ── Uniqueness guard ────────────────────────────────────────────────────────
  test('registering a duplicate Staff ID is rejected', async () => {
    const r = await admin.post('/api/staff', { data: {
      staffId, firstName: 'Dup', lastName: 'Licate', email: null, password: 'Test@1234', roles: [roleId],
    } });
    expectRejected(r.status(), 'duplicate Staff ID');
  });

  // ── Auth guards ─────────────────────────────────────────────────────────────
  test('wrong password is rejected (401)', async () => {
    const fresh = await pwRequest.newContext({ baseURL: API });
    const r = await fresh.post('/api/auth/login', { data: { staffId: ADMIN.staffId, password: 'WrongPass!' } });
    expectRejected(r.status(), 'wrong password');
    await fresh.dispose();
  });

  test('an unauthenticated request to a protected endpoint is rejected', async () => {
    const anon = await pwRequest.newContext({ baseURL: API });
    const r = await anon.get('/api/staff?page=1&pageSize=10');
    expect(r.status(), 'no-auth protected endpoint').toBe(401);
    await anon.dispose();
  });
});
