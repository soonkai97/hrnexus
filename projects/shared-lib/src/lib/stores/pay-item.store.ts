import { Injectable, inject, signal } from '@angular/core';
import { EMPTY, Observable, catchError, tap } from 'rxjs';
import { PayrollService } from '../services/payroll.service';
import { ToastService } from '../services/toast.service';
import { apiErrorMessage } from '../auth/error.interceptor';
import { AddPayItemRequest, PayItem } from '../models/payroll-run.model';

/**
 * One person's recurring allowances and deductions. The page reads these signals and runs these actions; it never calls the API.
 * An action returns an observable that emits once when it worked and completes empty when it did not (the reason has already been
 * shown), so a page can close its dialog in `subscribe(() => ...)`.
 */
@Injectable({ providedIn: 'root' })
export class PayItemStore {
  private readonly svc = inject(PayrollService);
  private readonly toast = inject(ToastService);

  readonly #items = signal<PayItem[]>([]);
  readonly #saving = signal(false);
  readonly items = this.#items.asReadonly();
  readonly saving = this.#saving.asReadonly();

  private staffId = '';

  /** Reads a person's items. A slower answer for someone the page has already left is dropped. */
  load(staffId: string): void {
    if (staffId !== this.staffId) this.#items.set([]);
    this.staffId = staffId;
    this.svc.getPayItems(staffId).subscribe({
      next: (rows) => {
        if (staffId === this.staffId) this.#items.set(rows);
      },
      error: (e: unknown) =>
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load allowances and deductions.'),
    });
  }

  add(staffId: string, req: AddPayItemRequest): Observable<unknown> {
    return this.change(staffId, this.svc.addPayItem(staffId, req), 'Added.', 'Failed to add it.');
  }

  /** Sets the end date of an item (also how a running item is stopped). */
  end(staffId: string, itemId: string, date: string): Observable<unknown> {
    return this.change(
      staffId,
      this.svc.endPayItem(staffId, itemId, date),
      'End date saved.',
      'Failed to end it.',
    );
  }

  /** Deletes an item entered by mistake (the server refuses one a submitted or locked run has used). */
  remove(staffId: string, itemId: string): Observable<unknown> {
    return this.change(
      staffId,
      this.svc.deletePayItem(staffId, itemId),
      'Deleted.',
      'Failed to delete it.',
    );
  }

  private change(
    staffId: string,
    call: Observable<unknown>,
    done: string,
    failed: string,
  ): Observable<unknown> {
    if (this.#saving()) return EMPTY;
    this.#saving.set(true);
    return call.pipe(
      tap(() => {
        this.#saving.set(false);
        this.toast.success(done);
        this.load(staffId);
      }),
      catchError((e: unknown) => {
        this.#saving.set(false);
        this.toast.error(apiErrorMessage(e) ?? failed);
        return EMPTY;
      }),
    );
  }
}
