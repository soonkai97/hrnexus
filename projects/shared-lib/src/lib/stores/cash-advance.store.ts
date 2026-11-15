import { Injectable, inject, signal } from '@angular/core';
import { EMPTY, Observable, catchError, tap } from 'rxjs';
import {
  CashAdvanceDto,
  CashAdvanceService,
  CreateCashAdvanceRequest,
} from '../services/cash-advance.service';
import { ToastService } from '../services/toast.service';
import { apiErrorMessage } from '../auth/error.interceptor';

/**
 * Cash advances. The page reads these signals and runs these actions; it never calls the API. An action returns an observable that
 * emits once when it worked and completes empty when it did not (the reason has already been shown).
 */
@Injectable({ providedIn: 'root' })
export class CashAdvanceStore {
  private readonly svc = inject(CashAdvanceService);
  private readonly toast = inject(ToastService);

  readonly #advances = signal<CashAdvanceDto[]>([]);
  readonly #loading = signal(false);
  readonly #saving = signal(false);
  readonly #busyId = signal<string | null>(null);
  readonly advances = this.#advances.asReadonly();
  readonly loading = this.#loading.asReadonly();
  /** True while a new advance or a schedule is being saved. */
  readonly saving = this.#saving.asReadonly();
  /** The advance an action is running on (settle, reopen, delete). */
  readonly busyId = this.#busyId.asReadonly();

  private status: string | undefined;

  /** Reads the list, optionally only one status ('outstanding' or 'settled'). */
  load(status?: string): void {
    this.status = status;
    this.#loading.set(true);
    this.svc.list(status).subscribe({
      next: (list) => {
        if (status !== this.status) return; // an answer for a filter the person already left
        this.#advances.set(list);
        this.#loading.set(false);
      },
      error: () => {
        this.toast.error('Failed to load cash advances.');
        this.#loading.set(false);
      },
    });
  }

  create(req: CreateCashAdvanceRequest): Observable<unknown> {
    return this.save(this.svc.create(req), 'Cash advance recorded.', 'Failed to save.');
  }

  /** How much payroll takes back each month (null = everything still owed, in the next run). */
  setRepayment(id: string, perMonth: number | null): Observable<unknown> {
    return this.save(this.svc.setRepayment(id, perMonth), 'Repayment saved.', 'Failed to save.');
  }

  setSettled(id: string, settled: boolean): Observable<unknown> {
    return this.onOne(
      id,
      this.svc.setSettled(id, settled),
      settled ? 'Marked as settled.' : 'Reopened.',
      'Failed to save.',
    );
  }

  remove(id: string): Observable<unknown> {
    return this.onOne(id, this.svc.delete(id), 'Cash advance deleted.', 'Failed to delete.');
  }

  private save(call: Observable<unknown>, done: string, failed: string): Observable<unknown> {
    if (this.#saving()) return EMPTY;
    this.#saving.set(true);
    return call.pipe(
      tap(() => {
        this.#saving.set(false);
        this.toast.success(done);
        this.load(this.status);
      }),
      catchError((e: unknown) => {
        this.#saving.set(false);
        this.toast.error(apiErrorMessage(e) ?? failed);
        return EMPTY;
      }),
    );
  }

  private onOne(
    id: string,
    call: Observable<unknown>,
    done: string,
    failed: string,
  ): Observable<unknown> {
    if (this.#busyId()) return EMPTY;
    this.#busyId.set(id);
    return call.pipe(
      tap(() => {
        this.#busyId.set(null);
        this.toast.success(done);
        this.load(this.status);
      }),
      catchError((e: unknown) => {
        this.#busyId.set(null);
        this.toast.error(apiErrorMessage(e) ?? failed);
        return EMPTY;
      }),
    );
  }
}
