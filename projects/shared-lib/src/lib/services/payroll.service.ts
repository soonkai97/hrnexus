import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AddSalaryRequest, SalaryOverview, SetEmploymentRequest } from '../models/payroll.model';
import { AddPayItemRequest, PayItem } from '../models/payroll-run.model';

/**
 * Salary profile and employment details for one staff member (payroll.view to read,
 * payroll.manage to write). Salary history is append-only: rows are added, never edited.
 */
@Injectable({ providedIn: 'root' })
export class PayrollService {
  private readonly http = inject(HttpClient);

  private url(staffId: string, part: string): string {
    return `/api/staff/${encodeURIComponent(staffId)}/${part}`;
  }

  getSalary(staffId: string): Observable<SalaryOverview> {
    return this.http.get<SalaryOverview>(this.url(staffId, 'salary'));
  }

  addSalary(staffId: string, body: AddSalaryRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.url(staffId, 'salary'), body);
  }

  // ── recurring allowances and deductions ─────────────────────────────────────

  getPayItems(staffId: string): Observable<PayItem[]> {
    return this.http.get<PayItem[]>(this.url(staffId, 'pay-items'));
  }

  addPayItem(staffId: string, body: AddPayItemRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.url(staffId, 'pay-items'), body);
  }

  /** Sets the last day the item applies (how an item a payroll run has used stops). */
  endPayItem(staffId: string, itemId: string, to: string): Observable<void> {
    return this.http.put<void>(this.url(staffId, `pay-items/${encodeURIComponent(itemId)}/end`), {
      to,
    });
  }

  deletePayItem(staffId: string, itemId: string): Observable<void> {
    return this.http.delete<void>(this.url(staffId, `pay-items/${encodeURIComponent(itemId)}`));
  }

  setEmployment(staffId: string, body: SetEmploymentRequest): Observable<void> {
    return this.http.put<void>(this.url(staffId, 'employment'), body);
  }
}
