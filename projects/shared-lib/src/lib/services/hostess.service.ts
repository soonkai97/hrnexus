import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  HostessEntryDto,
  HostessExpenseDto,
  HostessLoanDto,
  HostessSummaryDto,
  UpsertEntryRequest,
  UpsertExpenseRequest,
  UpsertLoanRequest,
} from '../models/hostess.model';

@Injectable({ providedIn: 'root' })
export class HostessService {
  private readonly http = inject(HttpClient);

  // ── Entries ─────────────────────────────────────────────────────────────────

  listEntries(staffId: string, year: number, month: number): Observable<HostessEntryDto[]> {
    return this.http.get<HostessEntryDto[]>('/api/hostess-entries', {
      params: { staffId, year, month },
    });
  }

  createEntry(req: UpsertEntryRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/hostess-entries', req);
  }

  updateEntry(id: string, req: UpsertEntryRequest): Observable<void> {
    return this.http.put<void>(`/api/hostess-entries/${id}`, req);
  }

  deleteEntry(id: string): Observable<void> {
    return this.http.delete<void>(`/api/hostess-entries/${id}`);
  }

  // ── Expenses ─────────────────────────────────────────────────────────────────

  listExpenses(staffId: string, year: number, month: number): Observable<HostessExpenseDto[]> {
    return this.http.get<HostessExpenseDto[]>('/api/hostess-entries/expenses', {
      params: { staffId, year, month },
    });
  }

  createExpense(req: UpsertExpenseRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/hostess-entries/expenses', req);
  }

  updateExpense(id: string, req: UpsertExpenseRequest): Observable<void> {
    return this.http.put<void>(`/api/hostess-entries/expenses/${id}`, req);
  }

  deleteExpense(id: string): Observable<void> {
    return this.http.delete<void>(`/api/hostess-entries/expenses/${id}`);
  }

  // ── Loans ────────────────────────────────────────────────────────────────────

  listLoans(staffId: string): Observable<HostessLoanDto[]> {
    return this.http.get<HostessLoanDto[]>('/api/hostess-entries/loans', {
      params: { staffId },
    });
  }

  createLoan(req: UpsertLoanRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/hostess-entries/loans', req);
  }

  deleteLoan(id: string): Observable<void> {
    return this.http.delete<void>(`/api/hostess-entries/loans/${id}`);
  }

  // ── Summary ──────────────────────────────────────────────────────────────────

  summary(year: number, month: number): Observable<HostessSummaryDto[]> {
    return this.http.get<HostessSummaryDto[]>('/api/hostess-entries/summary', {
      params: { year, month },
    });
  }
}
