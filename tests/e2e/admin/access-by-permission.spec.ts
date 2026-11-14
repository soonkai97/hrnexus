import { test, expect, request as pwRequest, type APIRequestContext } from '@playwright/test';
import { ensureRole, futureWeekday } from '../shared/api';

/**
 * Phase 7o: access is decided by permissions, never by what a role is called.
 *
 * WHY this matters: a pending-leave notice used to go to people whose role was NAMED manager or administrator,
 * so a custom role holding `leave.approve` never heard about leave waiting for it. And the first administrator
 * role, which used to be protected by its name, must stay protected by what it IS (flagged, always holds every
 * permission, cannot be deleted) even though it can now be renamed.
 */

const ADMIN = { staffId: 'HRADMIN', password: 'Admin@1234' };
const API = process.env['BASE_URL'] ?? 'http://localhost:8080';
const PASSWORD = 'Test@1234';

interface Notice {
  type: string;
  title: string;
  relatedId: string | null;
}
interface RoleRow {
  id: string;
  name: string;
  isSystem: boolean;
}

async function signIn(staffId: string, password: string): Promise<APIRequestContext> {
  const ctx = await pwRequest.newContext({ baseURL: API });
  const res = await ctx.post('/api/auth/login', { data: { staffId, password } });
  expect(res.ok(), `sign in ${staffId} (${res.status()})`).toBeTruthy();
  return ctx;
}

test.describe.serial('Access follows permissions, not role names', () => {
  let admin: APIRequestContext;
  const uid = Date.now().toString().slice(-7);
  const approverRole = `APPROVER${uid}`;
  const plainRole = `PLAIN${uid}`;
  const created: string[] = [];

  async function hire(staffId: string, roleId: string): Promise<string> {
    const res = await admin.post('/api/staff', {
      data: {
        staffId,
        firstName: 'Perm',
        lastName: 'Test',
        email: null,
        password: PASSWORD,
        roles: [roleId],
      },
    });
    expect(res.ok(), `hire ${staffId} (${res.status()}): ${await res.text()}`).toBeTruthy();
    const id = (await res.json()).id as string;
    created.push(id);
    return id;
  }

  test.beforeAll(async () => {
    admin = await signIn(ADMIN.staffId, ADMIN.password);
  });

  test.afterAll(async () => {
    for (const id of created) await admin.delete(`/api/staff/${id}`);
    await admin.dispose();
  });

  test('a custom role holding leave.approve is told when leave is applied for; a role without it is not', async () => {
    const approverRoleId = await ensureRole(admin, approverRole);
    const plainRoleId = await ensureRole(admin, plainRole);
    const grant = await admin.put(`/api/permissions/roles/${approverRole}`, {
      data: { permissions: ['leave.approve'] },
    });
    expect(grant.status(), 'grant leave.approve to the custom role').toBe(204);

    await hire(`AP${uid}`, approverRoleId);
    await hire(`DC${uid}`, plainRoleId); // a decoy with no permission at all
    await hire(`AL${uid}`, plainRoleId); // the applicant

    const applicant = await signIn(`AL${uid}`, PASSWORD);
    const day = futureWeekday(200 + Math.floor(Math.random() * 100));
    const apply = await applicant.post('/api/leave', {
      data: {
        leaveType: 'unpaid',
        startDate: day,
        endDate: day,
        startTime: '09:00',
        endTime: '17:00',
        hours: 8,
        notes: 'perm e2e',
      },
    });
    expect(apply.ok(), `apply (${apply.status()})`).toBeTruthy();
    const leaveId = (await apply.json()).id as string;

    const approver = await signIn(`AP${uid}`, PASSWORD);
    const mine = (await (await approver.get('/api/notifications')).json()) as Notice[];
    expect(
      mine.some((n) => n.type === 'leave_pending' && n.relatedId === leaveId),
      'the approver is told about the pending leave',
    ).toBeTruthy();

    const decoy = await signIn(`DC${uid}`, PASSWORD);
    const decoyNotices = (await (await decoy.get('/api/notifications')).json()) as Notice[];
    expect(
      decoyNotices.some((n) => n.relatedId === leaveId),
      'a role without leave.approve is not told',
    ).toBeFalsy();

    const own = (await (await applicant.get('/api/notifications')).json()) as Notice[];
    expect(
      own.some((n) => n.type === 'leave_pending' && n.relatedId === leaveId),
      'the applicant is not told about their own request',
    ).toBeFalsy();

    await applicant.delete(`/api/leave/${leaveId}`);
    await Promise.all([applicant.dispose(), approver.dispose(), decoy.dispose()]);
  });

  test('the first administrator role can be renamed but not deleted or stripped of permissions', async () => {
    const roles = (await (await admin.get('/api/staff/roles')).json()) as RoleRow[];
    const first = roles.find((r) => r.isSystem);
    expect(first, 'a fresh or migrated install has the first administrator role').toBeTruthy();
    const { id, name } = first!;

    const edit = await admin.put(`/api/permissions/roles/${encodeURIComponent(name)}`, {
      data: { permissions: ['staff.view'] },
    });
    expect(edit.status(), 'its permissions cannot be edited').toBe(400);
    expect((await edit.json()).error).toMatch(/every permission/i);

    const del = await admin.delete(`/api/staff/roles/${id}`);
    expect(del.status(), 'it cannot be deleted').toBe(409);

    try {
      const renamed = await admin.patch(`/api/staff/roles/${id}`, {
        data: { name: `CHIEF${uid}` },
      });
      expect(renamed.status(), 'it can be renamed: nothing looks it up by name').toBe(204);
      const again = (await (await admin.get('/api/staff/roles')).json()) as RoleRow[];
      expect(again.find((r) => r.id === id)?.name).toBe(`CHIEF${uid}`);
      // HRADMIN still holds every permission through it, whatever it is called.
      const perms = (await (await admin.get('/api/auth/me/permissions')).json()) as string[];
      expect(perms).toContain('roles.manage');
    } finally {
      await admin.patch(`/api/staff/roles/${id}`, { data: { name } }); // put the name back
    }
  });
});
