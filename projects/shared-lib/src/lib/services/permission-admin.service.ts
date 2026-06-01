import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** A per-user permission override: grant (true) adds, revoke (false) removes vs role defaults. */
export interface StaffOverride {
  permissionKey: string;
  granted: boolean;
}

/** An ordered, labelled grouping of permission keys for display in the editors. */
export interface PermissionGroup { labelKey: string; keys: string[]; }

/** Permission catalog organised into sections (display order). Every catalog key belongs to one. */
// Mirrors the sidebar nav grouping (Access + Operations / Finance / Communication / Setup) so the
// permission editors read the same way as the menu. Each key sits in the same group as the page it
// gates: Announcements → Communication, Payroll Locks → Setup, etc. 'access' has no nav page (it's the
// portal gate). Keep this in lockstep with ShellComponent.navGroupOrder + the page→group assignments.
export const PERMISSION_GROUPS: readonly PermissionGroup[] = [
  { labelKey: 'permissions.groups.access',     keys: ['portal.admin'] },
  { labelKey: 'permissions.groups.operations', keys: ['staff.view', 'staff.manage', 'leave.approve', 'leave.manage', 'worklog.manage', 'reports.view'] },
  { labelKey: 'permissions.groups.finance',    keys: ['payroll.view', 'payroll.manage'] },
  { labelKey: 'permissions.groups.setup',      keys: ['roles.manage', 'locations.manage', 'payroll.lock'] },
];

/**
 * Projects a flat set of visible permission keys onto the ordered groups, keeping only the keys
 * present in <paramref/> and dropping groups left empty. Any key not in a defined group is appended
 * to a trailing "other" group so nothing is ever silently hidden.
 */
export function groupedPermissions(visibleKeys: string[]): PermissionGroup[] {
  const set = new Set(visibleKeys);
  const grouped = PERMISSION_GROUPS
    .map(g => ({ labelKey: g.labelKey, keys: g.keys.filter(k => set.has(k)) }))
    .filter(g => g.keys.length > 0);
  const known = new Set(PERMISSION_GROUPS.flatMap(g => g.keys));
  const other = visibleKeys.filter(k => !known.has(k));
  if (other.length) grouped.push({ labelKey: 'permissions.groups.other', keys: other });
  return grouped;
}

/**
 * Admin CRUD for the permission framework: the catalog, each role's default permissions, and
 * per-user overrides. Separate from PermissionService (which loads the CURRENT user's effective
 * set); this one manages OTHER roles'/users' permissions. Backed by /api/permissions.
 */
@Injectable({ providedIn: 'root' })
export class PermissionAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/permissions';

  /** All assignable permission keys (code-defined catalog). */
  catalog(): Observable<string[]> {
    return this.http.get<string[]>(this.base);
  }

  getRolePermissions(roleName: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/roles/${encodeURIComponent(roleName)}`);
  }

  setRolePermissions(roleName: string, permissions: string[]): Observable<void> {
    return this.http.put<void>(`${this.base}/roles/${encodeURIComponent(roleName)}`, { permissions });
  }

  getUserOverrides(employeeId: string): Observable<StaffOverride[]> {
    return this.http.get<StaffOverride[]>(`${this.base}/users/${employeeId}`);
  }

  setUserOverrides(employeeId: string, overrides: StaffOverride[]): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${employeeId}`, { overrides });
  }
}
