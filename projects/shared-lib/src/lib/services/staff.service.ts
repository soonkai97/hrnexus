import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Staff, RegisterStaffRequest, StaffDetail, UpdateStaffRequest, StaffRole } from '../models';

export interface VenueLocation { id: string; name: string; }

@Injectable({ providedIn: 'root' })
export class StaffService {
  constructor(private http: HttpClient) {}

  getAll(search?: string): Observable<Staff[]> {
    const params = search ? `?search=${encodeURIComponent(search)}` : '';
    return this.http.get<Staff[]>(`/api/staff${params}`);
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

  getLocations(): Observable<VenueLocation[]> {
    return this.http.get<VenueLocation[]>('/api/locations');
  }

  /** Employee: get own full profile */
  getMyProfile(): Observable<StaffDetail> {
    return this.http.get<StaffDetail>('/api/auth/me');
  }
}
