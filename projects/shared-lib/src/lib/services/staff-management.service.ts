import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Staff, RegisterStaffRequest, StaffDetail, UpdateStaffRequest, StaffRole, PayType, PagedResult } from '../models';

export interface VenueLocation { id: string; name: string; }

@Injectable({ providedIn: 'root' })
export class StaffManagementService {
  private readonly http = inject(HttpClient);

  getAll(search?: string, page = 1, pageSize = 10, includeHidden = false): Observable<PagedResult<Staff>> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    if (search) params = params.set('search', search);
    if (includeHidden) params = params.set('includeHidden', 'true');
    return this.http.get<PagedResult<Staff>>('/api/staff', { params });
  }

  /** Show or hide a staff account in the admin roster (presentational only — does not deactivate). */
  setHidden(id: string, hidden: boolean): Observable<void> {
    return this.http.patch<void>(`/api/staff/${id}/hidden`, { hidden });
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

  createRole(name: string, hourlyRate: number = 0, nameZh: string | null = null, nameVi: string | null = null, payType: PayType = 'hourly'): Observable<void> {
    return this.http.post<void>('/api/staff/roles', { name, hourlyRate, nameZh, nameVi, payType });
  }

  updateRoleRate(id: string, hourlyRate: number, nameZh: string | null = null, nameVi: string | null = null, payType: PayType = 'hourly', name: string | null = null): Observable<void> {
    return this.http.patch<void>(`/api/staff/roles/${encodeURIComponent(id)}/rate`, { hourlyRate, nameZh, nameVi, payType, name });
  }

  deleteRole(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff/roles/${encodeURIComponent(id)}`);
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

  /** Additively assign one or more roles (by GUID id) to all selected staff. */
  bulkAssignRoles(staffIds: string[], roles: string[]): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/staff/bulk/roles', { staffIds, roles });
  }

  /** Deactivate all selected staff (backend refuses if it would remove the last active admin). */
  bulkDeactivate(staffIds: string[]): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/staff/bulk/deactivate', { staffIds });
  }

  getLocations(): Observable<VenueLocation[]> {
    return this.http.get<VenueLocation[]>('/api/locations');
  }

  /** Employee: get own full profile */
  getMyProfile(): Observable<StaffDetail> {
    return this.http.get<StaffDetail>('/api/auth/me');
  }
}
