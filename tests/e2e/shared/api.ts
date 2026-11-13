import type { APIRequestContext } from '@playwright/test';

/**
 * The staff API identifies roles by GUID: `POST /api/staff` takes `roles: Guid[]`,
 * `DELETE /api/staff/roles/{id}` takes the id, and `POST /api/staff/roles` returns 204 with no body.
 * Role names are stored upper-case. These helpers resolve a role id by name so specs can keep
 * thinking in names.
 */
interface RoleDto {
  id: string;
  name: string;
}

export async function roleIdByName(
  api: APIRequestContext,
  name: string,
): Promise<string | undefined> {
  const res = await api.get('/api/staff/roles');
  if (!res.ok()) throw new Error(`GET /api/staff/roles failed (${res.status()})`);
  const roles = (await res.json()) as RoleDto[];
  return roles.find((r) => r.name.toUpperCase() === name.toUpperCase())?.id;
}

/** Returns the id of the named role, creating the role first when it does not exist. */
export async function ensureRole(api: APIRequestContext, name: string): Promise<string> {
  const existing = await roleIdByName(api, name);
  if (existing) return existing;

  const created = await api.post('/api/staff/roles', { data: { name } });
  if (!created.ok() && created.status() !== 409) {
    throw new Error(`create role ${name} failed (${created.status()}): ${await created.text()}`);
  }

  const id = await roleIdByName(api, name);
  if (!id) throw new Error(`role ${name} not found after creating it`);
  return id;
}

/**
 * yyyy-MM-dd of the first weekday on or after `daysAhead` days from now. Leave is charged by
 * working days (Mon-Fri), so a single-day request on a weekend is rejected; tests that need an
 * accepted request must not land on one.
 */
export function futureWeekday(daysAhead: number): string {
  let d = new Date(Date.now() + daysAhead * 86_400_000);
  while (d.getUTCDay() === 0 || d.getUTCDay() === 6) d = new Date(d.getTime() + 86_400_000);
  return d.toISOString().slice(0, 10);
}
