import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StatutoryRate } from '../models/payroll-rates.model';

/**
 * Statutory rate table (EPF, SOCSO, EIS, HRD Corp, minimum wage). Reading needs payroll.view;
 * verifying a row and switching the optional HRD levy need payroll.rates.verify.
 */
@Injectable({ providedIn: 'root' })
export class PayrollRatesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payroll/rates';

  list(): Observable<StatutoryRate[]> {
    return this.http.get<StatutoryRate[]>(this.base);
  }

  verify(id: string, note: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/verify`, { note });
  }

  setActive(id: string, active: boolean): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/active`, { active });
  }
}
