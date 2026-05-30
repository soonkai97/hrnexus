import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** A per-user permission override: grant (true) adds, revoke (false) removes vs role defaults. */
export interface StaffOverride {
  permissionKey: string;
  granted: boolean;
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
