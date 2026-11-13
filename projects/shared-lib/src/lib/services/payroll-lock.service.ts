import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** A locked payroll period (one calendar month). */
export interface PayrollLockDto {
  year:     number;
  month:    number;   // 1-12
  lockedAt: string;   // ISO timestamp
  lockedBy: string;
  note:     string | null;
}

/**
 * Manage payroll-period locks. Locking a month freezes its payroll records on the backend
 * (manager/admin only). Used by the dedicated locks page.
 */
@Injectable({ providedIn: 'root' })
export class PayrollLockService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payroll-locks';

  list(year?: number): Observable<PayrollLockDto[]> {
    const url = year ? `${this.base}?year=${year}` : this.base;
    return this.http.get<PayrollLockDto[]>(url);
  }

  lock(year: number, month: number, note?: string | null): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.base, { year, month, note: note ?? null });
  }

  reopen(year: number, month: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${year}/${month}`);
  }
}
