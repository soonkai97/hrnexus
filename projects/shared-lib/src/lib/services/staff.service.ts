import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  StaffEntryDto,
  StaffExpenseDto,
  StaffLoanDto,
  StaffSummaryDto,
  StaffLedgerPayoutDto,
  StaffOutstandingDto,
  UpsertEntryRequest,
  UpsertExpenseRequest,
  UpsertLoanRequest,
} from '../models/staff-ledger.model';

@Injectable({ providedIn: 'root' })
export class StaffService {
  private readonly http = inject(HttpClient);

  // ── Entries ─────────────────────────────────────────────────────────────────

  listEntries(staffId: string, year: number, month: number): Observable<StaffEntryDto[]> {
    return this.http.get<StaffEntryDto[]>('/api/staff-entries', {
      params: { staffId, year, month },
    });
  }

  createEntry(req: UpsertEntryRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff-entries', req);
  }

  updateEntry(id: string, req: UpsertEntryRequest): Observable<void> {
    return this.http.put<void>(`/api/staff-entries/${id}`, req);
  }

  deleteEntry(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff-entries/${id}`);
  }

  // ── Expenses ─────────────────────────────────────────────────────────────────

  listExpenses(staffId: string, year: number, month: number): Observable<StaffExpenseDto[]> {
    return this.http.get<StaffExpenseDto[]>('/api/staff-entries/expenses', {
      params: { staffId, year, month },
    });
  }

  createExpense(req: UpsertExpenseRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff-entries/expenses', req);
  }

  updateExpense(id: string, req: UpsertExpenseRequest): Observable<void> {
    return this.http.put<void>(`/api/staff-entries/expenses/${id}`, req);
  }

  deleteExpense(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff-entries/expenses/${id}`);
  }

  // ── Loans ────────────────────────────────────────────────────────────────────

  listLoans(staffId: string): Observable<StaffLoanDto[]> {
    return this.http.get<StaffLoanDto[]>('/api/staff-entries/loans', {
      params: { staffId },
    });
  }

  createLoan(req: UpsertLoanRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff-entries/loans', req);
  }

  deleteLoan(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff-entries/loans/${id}`);
  }

  // ── Summary ──────────────────────────────────────────────────────────────────

  summary(year: number, month: number): Observable<StaffLedgerPayoutDto[]> {
    return this.http.get<StaffLedgerPayoutDto[]>('/api/staff-entries/summary', {
      params: { year, month },
    });
  }

  outstanding(): Observable<StaffOutstandingDto[]> {
    return this.http.get<StaffOutstandingDto[]>('/api/staff-entries/outstanding');
  }

  bulkSettle(entryIds: string[], settlement: string): Observable<{ updated: number }> {
    return this.http.put<{ updated: number }>('/api/staff-entries/bulk-settle', { entryIds, settlement });
  }
}
