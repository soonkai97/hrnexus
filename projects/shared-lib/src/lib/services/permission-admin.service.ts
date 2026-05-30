import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FeatureKey } from './feature-flag.service';

/** A per-user permission override: grant (true) adds, revoke (false) removes vs role defaults. */
export interface StaffOverride {
  permissionKey: string;
  granted: boolean;
}

/**
 * Permissions that only apply when a feature flag is enabled. The editors hide these keys when the
 * feature is off; the backend catalog stays complete, and any already-assigned rows are preserved
 * on save (only the editable DISPLAY is filtered, not the saved set).
 */
export const PERMISSION_FEATURE_FLAG: Readonly<Record<string, FeatureKey>> = {
  'payroll.view':         'payroll',
  'payroll.manage':       'payroll',
  'payroll.lock':         'payroll',
  'tips.manage':          'tips',
  'announcements.manage': 'announcements',
  'ledger.manage':        'staffLedger',
};

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
