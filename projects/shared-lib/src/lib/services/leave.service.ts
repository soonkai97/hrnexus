import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { LeaveBalance, LeaveRecord, StaffLeaveBalance, UpsertLeaveRequest } from '../models';

export interface LeavePagedResult {
  items: LeaveRecord[];
  total: number;
}

export interface ApplyLeaveRequest {
  leaveType: string;
  startDate:  string;
  endDate:    string;
  startTime:  string | null;  // HH:mm
  endTime:    string | null;  // HH:mm
  hours:      number | null;
  notes:      string | null;
}

@Injectable({ providedIn: 'root' })
export class LeaveService {
  constructor(private http: HttpClient) {}

  // ─── Admin endpoints ────────────────────────────────────────────────────────

  getAll(params: {
    employeeId?: string;
    dateFrom?: string;
    dateTo?: string;
    status?: string;
    leaveType?: string;
    page?: number;
    pageSize?: number;
  } = {}): Observable<LeavePagedResult> {
    let p = new HttpParams();
    if (params.employeeId) p = p.set('employeeId', params.employeeId);
    if (params.dateFrom)   p = p.set('dateFrom',   params.dateFrom);
    if (params.dateTo)     p = p.set('dateTo',     params.dateTo);
    if (params.status)     p = p.set('status',     params.status);
    if (params.leaveType)  p = p.set('leaveType',  params.leaveType);
    if (params.page)       p = p.set('page',       params.page.toString());
    if (params.pageSize)   p = p.set('pageSize',   params.pageSize.toString());
    return this.http.get<LeavePagedResult>('/api/admin/leave', { params: p });
  }

  create(req: UpsertLeaveRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/admin/leave', req);
  }

  update(id: string, req: UpsertLeaveRequest): Observable<void> {
    return this.http.put<void>(`/api/admin/leave/${id}`, req);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/admin/leave/${id}`);
  }

  // ─── Employee-facing endpoints ───────────────────────────────────────────────

  /** Get the current employee's own leave requests. */
  getMine(): Observable<LeaveRecord[]> {
    return this.http.get<LeaveRecord[]>('/api/leave/mine');
  }

  /** Apply for leave (always sets status = pending). */
  apply(req: ApplyLeaveRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/leave', req);
  }

  /** Cancel a pending leave (employee can only cancel their own). */
  cancel(id: string): Observable<void> {
    return this.http.delete<void>(`/api/leave/${id}`);
  }

  // ── Balance ────────────────────────────────────────────────────────────────

  /** Get the current employee's leave balance. */
  getMyBalance(year?: number): Observable<LeaveBalance> {
    const q = year ? `?year=${year}` : '';
    return this.http.get<LeaveBalance>(`/api/leave/balance${q}`);
  }

  /** Admin: get leave balances for all active staff. */
  getAllBalances(year?: number): Observable<StaffLeaveBalance[]> {
    const q = year ? `?year=${year}` : '';
    return this.http.get<StaffLeaveBalance[]>(`/api/admin/leave-balance${q}`);
  }

  /** Admin: get any employee's leave balance. */
  getBalance(employeeId: string, year?: number): Observable<LeaveBalance> {
    const q = year ? `?year=${year}` : '';
    return this.http.get<LeaveBalance>(`/api/admin/leave-balance/${employeeId}${q}`);
  }

  /** Admin: set an employee's entitlement for a year (values in hours). */
  setEntitlement(employeeId: string, year: number, annualHours: number, sickHours: number): Observable<void> {
    return this.http.put<void>(`/api/admin/leave-balance/${employeeId}`,
      { year, annualHours, sickHours });
  }

  /** Admin: apply the same entitlement to all active staff for a given year. */
  bulkSetEntitlement(year: number, annualHours: number, sickHours: number): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/admin/leave-balance/bulk',
      { year, annualHours, sickHours });
  }

  /** Admin: carry over unused annual leave from one year to the next (capped at maxCarryOver hours). */
  carryOverEntitlement(fromYear: number, toYear: number, maxCarryOver: number): Observable<{ count: number }> {
    return this.http.post<{ count: number }>('/api/admin/leave-balance/carry-over',
      { fromYear, toYear, maxCarryOver });
  }
}
