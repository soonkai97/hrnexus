import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface CashAdvanceDto {
  id:          string;
  employeeId:  string;
  staffId:     string;
  staffName:   string;
  amount:      number;
  advanceDate: string;        // yyyy-MM-dd
  reason:      string | null;
  status:      string;        // 'outstanding' | 'settled'
  settledAt:   string | null;
  createdAt:   string;
}

export interface CreateCashAdvanceRequest {
  employeeId:  string;
  amount:      number;
  advanceDate: string;        // yyyy-MM-dd
  reason?:     string | null;
}

/**
 * Cash advances — salary advances the venue records before payday (records only; moves no money).
 * Admin endpoints are permission-gated server-side; `listMine` is the staff read-only view.
 */
@Injectable({ providedIn: 'root' })
export class CashAdvanceService {
  private readonly http = inject(HttpClient);

  list(status?: string): Observable<CashAdvanceDto[]> {
    const url = status ? `/api/cash-advances?status=${encodeURIComponent(status)}` : '/api/cash-advances';
    return this.http.get<CashAdvanceDto[]>(url);
  }

  create(req: CreateCashAdvanceRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/cash-advances', req);
  }

  setSettled(id: string, settled: boolean): Observable<void> {
    return this.http.patch<void>(`/api/cash-advances/${id}/settled`, { settled });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/cash-advances/${id}`);
  }
}
