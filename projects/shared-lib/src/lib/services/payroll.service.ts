import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AddSalaryRequest, SalaryOverview, SetEmploymentRequest } from '../models/payroll.model';

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

  setEmployment(staffId: string, body: SetEmploymentRequest): Observable<void> {
    return this.http.put<void>(this.url(staffId, 'employment'), body);
  }
}
