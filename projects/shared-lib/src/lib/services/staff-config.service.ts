import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  StaffWorkTypeDto,
  StaffSettlementDto,
  UpsertWorkTypeRequest,
  UpsertSettlementRequest,
  StaffExpenseCategoryDto,
  UpsertExpenseCategoryRequest,
} from '../models/staff-ledger.model';

@Injectable({ providedIn: 'root' })
export class StaffConfigService {
  private readonly http = inject(HttpClient);

  readonly workTypes         = signal<StaffWorkTypeDto[]>([]);
  readonly settlements       = signal<StaffSettlementDto[]>([]);
  readonly expenseCategories = signal<StaffExpenseCategoryDto[]>([]);

  // ── Work Types ──────────────────────────────────────────────────────────────

  loadWorkTypes(): void {
    this.http.get<StaffWorkTypeDto[]>('/api/staff-config/work-types').subscribe({
      next: (data) => this.workTypes.set(data),
    });
  }

  createWorkType(req: UpsertWorkTypeRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff-config/work-types', req).pipe(
      tap(() => this.loadWorkTypes()),
    );
  }

  updateWorkType(id: string, req: UpsertWorkTypeRequest): Observable<void> {
    return this.http.put<void>(`/api/staff-config/work-types/${id}`, req).pipe(
      tap(() => this.loadWorkTypes()),
    );
  }

  deleteWorkType(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff-config/work-types/${id}`).pipe(
      tap(() => this.loadWorkTypes()),
    );
  }

  // ── Settlements ─────────────────────────────────────────────────────────────

  loadSettlements(): void {
    this.http.get<StaffSettlementDto[]>('/api/staff-config/settlements').subscribe({
      next: (data) => this.settlements.set(data),
    });
  }

  createSettlement(req: UpsertSettlementRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff-config/settlements', req).pipe(
      tap(() => this.loadSettlements()),
    );
  }

  updateSettlement(id: string, req: UpsertSettlementRequest): Observable<void> {
    return this.http.put<void>(`/api/staff-config/settlements/${id}`, req).pipe(
      tap(() => this.loadSettlements()),
    );
  }

  deleteSettlement(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff-config/settlements/${id}`).pipe(
      tap(() => this.loadSettlements()),
    );
  }

  // ── Expense Categories ──────────────────────────────────────────────────────

  loadExpenseCategories(): void {
    this.http.get<StaffExpenseCategoryDto[]>('/api/staff-config/expense-categories').subscribe({
      next: (data) => this.expenseCategories.set(data),
    });
  }

  createExpenseCategory(req: UpsertExpenseCategoryRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/staff-config/expense-categories', req).pipe(
      tap(() => this.loadExpenseCategories()),
    );
  }

  updateExpenseCategory(id: string, req: UpsertExpenseCategoryRequest): Observable<void> {
    return this.http.put<void>(`/api/staff-config/expense-categories/${id}`, req).pipe(
      tap(() => this.loadExpenseCategories()),
    );
  }

  deleteExpenseCategory(id: string): Observable<void> {
    return this.http.delete<void>(`/api/staff-config/expense-categories/${id}`).pipe(
      tap(() => this.loadExpenseCategories()),
    );
  }
}
