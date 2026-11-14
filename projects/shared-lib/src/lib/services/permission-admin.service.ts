import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** A per-user permission override: grant (true) adds, revoke (false) removes vs role defaults. */
export interface StaffOverride {
  permissionKey: string;
  granted: boolean;
}

/** An ordered, labelled grouping of permission keys for display in the editors. */
export interface PermissionGroup { label: string; keys: string[]; }

/** Permission catalog organised into sections (display order). Every catalog key belongs to one. */
// Mirrors the sidebar nav grouping (Operations / Finance / Communication / Setup) so the
// permission editors read the same way as the menu. Each key sits in the same group as the page it
// gates: Announcements → Communication, Payroll Locks → Setup, etc.
// Keep this in lockstep with ShellComponent.navGroupOrder + the page→group assignments.
export const PERMISSION_GROUPS: readonly PermissionGroup[] = [
  { label: 'Operations', keys: ['staff.view', 'staff.manage', 'leave.approve', 'leave.manage', 'reports.view'] },
  { label: 'Finance',    keys: ['payroll.view', 'payroll.manage'] },
  { label: 'Setup',      keys: ['roles.manage', 'policy.manage', 'payroll.lock'] },
];

/** Display names and one-line explanations for each catalog permission key. */
export const PERMISSION_LABELS: Readonly<Record<string, string>> = {
  'cashadvance.manage': 'Manage cash advances',
  'staff.view': 'View staff',
  'staff.manage': 'Manage staff',
  'leave.approve': 'Approve leave',
  'leave.manage': 'Manage leave entitlements',
  'policy.manage': 'Manage company policy',
  'reports.view': 'View reports',
  'payroll.view': 'View payroll',
  'payroll.manage': 'Manage payroll',
  'payroll.lock': 'Lock payroll periods',
  'roles.manage': 'Manage roles & permissions',
};

export const PERMISSION_DESCRIPTIONS: Readonly<Record<string, string>> = {
  'cashadvance.manage': 'Record and settle staff cash advances.',
  'staff.view': 'See the staff list and open staff profiles (read-only).',
  'staff.manage': 'Add, edit, deactivate staff and reset their passwords.',
  'leave.approve': 'Approve or reject leave requests, and record leave for staff.',
  'leave.manage': "Set each staff member's yearly leave entitlement (annual / sick hours).",
  'policy.manage': 'Set the working week, hours per day, leave limits and the carry-over rule for the whole company.',
  'reports.view': 'Open the dashboard with the workforce and leave overview.',
  'payroll.view': "See a staff member's salary and pay history.",
  'payroll.manage': "Record a raise and set a staff member's employment details.",
  'payroll.lock': 'Lock or reopen a payroll month.',
  'roles.manage': 'Create, rename and delete roles, and assign these permissions.',
};

/** Falls back to the raw key so an unlisted permission is never shown blank. */
export const permissionLabel = (key: string): string => PERMISSION_LABELS[key] ?? key;
export const permissionDescription = (key: string): string => PERMISSION_DESCRIPTIONS[key] ?? '';

/**
 * Projects a flat set of visible permission keys onto the ordered groups, keeping only the keys
 * present in <paramref/> and dropping groups left empty. Any key not in a defined group is appended
 * to a trailing "other" group so nothing is ever silently hidden.
 */
export function groupedPermissions(visibleKeys: string[]): PermissionGroup[] {
  const set = new Set(visibleKeys);
  const grouped = PERMISSION_GROUPS
    .map(g => ({ label: g.label, keys: g.keys.filter(k => set.has(k)) }))
    .filter(g => g.keys.length > 0);
  const known = new Set(PERMISSION_GROUPS.flatMap(g => g.keys));
  const other = visibleKeys.filter(k => !known.has(k));
  if (other.length) grouped.push({ label: 'Other', keys: other });
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
