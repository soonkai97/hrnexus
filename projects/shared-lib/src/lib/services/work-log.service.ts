import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface WorkLogDto {
  id: string;
  roleName: string;
  locationId: string;
  locationName: string;
  workDate: string;      // yyyy-MM-dd
  startTime: string;     // HH:mm
  endDate: string | null; // yyyy-MM-dd (null = active; may differ from workDate for overnight/multi-day)
  endTime: string | null;
  hours: number;
  cost: number;
  isActive: boolean;
  isManualEntry: boolean;
  isEdited: boolean;
  notes: string | null;
}

export interface UpdateWorkLogRequest {
  roleName: string;
  locationId: string;
  workDate: string;    // yyyy-MM-dd
  startTime: string;   // HH:mm
  endDate: string;     // yyyy-MM-dd
  endTime: string;     // HH:mm
  notes?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
}

export interface StartWorkLogRequest {
  roleName: string;
  locationId: string;
  startTime: string;   // HH:mm
  notes?: string | null;
  /** GPS captured from the device at clock-in (omitted if the staff denies permission). */
  latitude?: number | null;
  longitude?: number | null;
}

export interface CreateManualWorkLogRequest {
  roleName: string;
  locationId: string;
  workDate: string;    // yyyy-MM-dd
  startTime: string;  // HH:mm
  endTime: string;    // HH:mm
  notes?: string | null;
}

@Injectable({ providedIn: 'root' })
export class WorkLogService {
  private readonly http = inject(HttpClient);

  getActive(): Observable<WorkLogDto | null> {
    return this.http.get<WorkLogDto | null>('/api/worklogs/active');
  }

  getMy(page = 1, pageSize = 10): Observable<PagedResult<WorkLogDto>> {
    return this.http.get<PagedResult<WorkLogDto>>(`/api/worklogs/my?page=${page}&pageSize=${pageSize}`);
  }

  start(req: StartWorkLogRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/worklogs/start', req);
  }

  createManual(req: CreateManualWorkLogRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/worklogs/entry', req);
  }

  end(id: string, endTime: string): Observable<void> {
    return this.http.put<void>(`/api/worklogs/${id}/end`, { endTime });
  }

  /** Push a fresh GPS fix for the caller's current shift (staff "Update location" button). */
  updateActiveLocation(latitude: number, longitude: number): Observable<void> {
    return this.http.patch<void>('/api/worklogs/active/location', { latitude, longitude });
  }

  /** Admin/manager closes an OPEN shift (staff forgot to clock out) at a chosen end date + time. */
  adminEndShift(id: string, endDate: string, endTime: string): Observable<void> {
    return this.http.put<void>(`/api/worklogs/${id}/admin-end`, { endDate, endTime });
  }

  update(id: string, req: UpdateWorkLogRequest): Observable<void> {
    return this.http.put<void>(`/api/worklogs/${id}`, req);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/worklogs/${id}`);
  }

  /** Admin: paginated work logs. Param names match backend query string exactly. */
  getAll(params?: {
    employeeId?: string;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  }): Observable<PagedResult<WorkLogDto>> {
    let p = new HttpParams();
    if (params?.employeeId) p = p.set('employeeId', params.employeeId);
    if (params?.from)       p = p.set('from',        params.from);
    if (params?.to)         p = p.set('to',          params.to);
    if (params?.page)       p = p.set('page',        params.page.toString());
    if (params?.pageSize)   p = p.set('pageSize',    params.pageSize.toString());
    return this.http.get<PagedResult<WorkLogDto>>('/api/worklogs', { params: p });
  }
}
