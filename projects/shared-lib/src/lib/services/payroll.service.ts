import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaySummary } from '../models';

@Injectable({ providedIn: 'root' })
export class PayrollService {
  constructor(private http: HttpClient) {}

  getMySummary(periodStart: string, periodEnd: string): Observable<PaySummary> {
    return this.http.get<PaySummary>(`/api/worklogs/pay-summary?start=${periodStart}&end=${periodEnd}`);
  }
}
