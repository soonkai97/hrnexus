import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Staff, RegisterStaffRequest, StaffDetail, UpdateStaffRequest, StaffRole } from '../models';
import { PagedResult } from './work-log.service';

export interface VenueLocation { id: string; name: string; }

export interface BulkStaffImportRow {
  staffId: string; firstName: string; lastName?: string | null; email?: string | null;
  roles: string[]; locations?: string[] | null; password?: string | null;
}
export interface BulkStaffRowResult {
  staffId: string; success: boolean; error: string | null; tempPassword: string | null;
}
export interface BulkStaffImportResult {
  created: number; failed: number; rows: BulkStaffRowResult[];
}

@Injectable({ providedIn: 'root' })
export class StaffManagementService {
  private readonly http = inject(HttpClient);

  getAll(search?: string, page = 1, pageSize = 10): Observable<PagedResult<Staff>> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    if (search) params = params.set('search', search);
    return this.http.get<PagedResult<Staff>>('/api/staff', { params });
  }

  getById(id: string): Observable<StaffDetail> {
    return this.http.get<StaffDetail>(`/api/staff/${id}`);
  }

  register(req: RegisterStaffRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff', req);
  }

  update(id: string, req: UpdateStaffRequest): Observable<void> {
    return this.http.put<void>(`/api/staff/${id}`, req);
  }

  toggleActive(id: string, activate: boolean): Observable<void> {
    return this.http.patch<void>(`/api/staff/${id}/${activate ? 'activate' : 'deactivate'}`, {});
  }

  getRoles(): Observable<StaffRole[]> {
    return this.http.get<StaffRole[]>('/api/staff/roles');
  }

  createRole(name: string, hourlyRate: number = 0): Observable<void> {
    return this.http.post<void>('/api/staff/roles', { name, hourlyRate });
  }

  updateRoleRate(name: string, hourlyRate: number): Observable<void> {
    return this.http.patch<void>(`/api/staff/roles/${encodeURIComponent(name)}/rate`, { hourlyRate });
  }

  deleteRole(name: string): Observable<void> {
    return this.http.delete<void>(`/api/staff/roles/${encodeURIComponent(name)}`);
  }

  resetPassword(id: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`/api/staff/${id}/reset-password`, { newPassword });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff/${id}`);
  }

  // ── Bulk actions (act on a selected set of staff at once) ───────────────────
  /** Additively assign one or more work locations to all selected staff. */
  bulkAssignLocations(staffIds: string[], locationIds: string[]): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/staff/bulk/locations', { staffIds, locationIds });
  }

  /** Additively assign one or more roles to all selected staff. */
  bulkAssignRoles(staffIds: string[], roles: string[]): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/staff/bulk/roles', { staffIds, roles });
  }

  /** Deactivate all selected staff (backend refuses if it would remove the last active admin). */
  bulkDeactivate(staffIds: string[]): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/staff/bulk/deactivate', { staffIds });
  }

  /** Create many custom roles at once from a list of {name, hourlyRate} (duplicates skipped). */
  bulkCreateRoles(roles: { name: string; hourlyRate: number }[]): Observable<{ created: number; skipped: number }> {
    return this.http.post<{ created: number; skipped: number }>('/api/staff/roles/bulk', { roles });
  }

  /** Import many staff at once (partial success). Returns per-row outcomes incl. generated passwords. */
  bulkImportStaff(rows: BulkStaffImportRow[]): Observable<BulkStaffImportResult> {
    return this.http.post<BulkStaffImportResult>('/api/staff/bulk-import', { rows });
  }

  getLocations(): Observable<VenueLocation[]> {
    return this.http.get<VenueLocation[]>('/api/locations');
  }

  /** Employee: get own full profile */
  getMyProfile(): Observable<StaffDetail> {
    return this.http.get<StaffDetail>('/api/auth/me');
  }
}
