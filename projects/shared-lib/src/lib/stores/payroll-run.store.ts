import { Injectable, computed, inject, signal } from '@angular/core';
import { EMPTY, Observable, catchError, map, tap } from 'rxjs';
import { PayrollRunService } from '../services/payroll-run.service';
import { ToastService } from '../services/toast.service';
import { apiErrorMessage } from '../auth/error.interceptor';
import {
  PayrollRunAction,
  PayrollRunDetail,
  PayrollRunLine,
  PayrollRunSummary,
  PayrollTeam,
  PayrollTeamRun,
} from '../models/payroll-run.model';

/** What a person can change on one line of a draft (the version is added by the store). */
export interface RunLineEdit {
  pcb: number | null;
  excluded: boolean;
  excludedReason: string | null;
}

/**
 * Everything the payroll run pages know, and everything they may do. Pages never call the API: they read these signals and run
 * these actions.
 *
 * Reads keep the latest answer only (a slow answer for a run or tab the person has already left is dropped). Every action sends the
 * version of the run as the store last read it, so an out-of-date page is refused (409); on any refusal the store says why and
 * reads the run again so the page is current. What the signed-in person may do comes from the server (`allowedActions`, and
 * `canSignOff` on a line); the store never works out who may do what.
 *
 * An action returns an observable that emits once when it worked and completes empty when it did not (the reason has already been
 * shown), so a page can close its dialog in `subscribe(() => ...)` and never needs an error callback.
 */
@Injectable({ providedIn: 'root' })
export class PayrollRunStore {
  private readonly svc = inject(PayrollRunService);
  private readonly toast = inject(ToastService);

  // ── the list ──────────────────────────────────────────────────────────────────
  readonly #runs = signal<PayrollRunSummary[]>([]);
  readonly #runsLoading = signal(false);
  readonly runs = this.#runs.asReadonly();
  readonly runsLoading = this.#runsLoading.asReadonly();

  // ── one run ───────────────────────────────────────────────────────────────────
  readonly #detail = signal<PayrollRunDetail | null>(null);
  readonly #detailLoading = signal(false);
  readonly #busy = signal(false);
  readonly detail = this.#detail.asReadonly();
  readonly detailLoading = this.#detailLoading.asReadonly();
  /** True while an action is running; only one runs at a time. */
  readonly busy = this.#busy.asReadonly();
  readonly summary = computed(() => this.#detail()?.summary ?? null);
  readonly lines = computed(() => this.#detail()?.lines ?? []);

  // ── a manager's view ──────────────────────────────────────────────────────────
  readonly #teamRuns = signal<PayrollTeamRun[]>([]);
  readonly #teamRunsLoading = signal(true);
  readonly #teamId = signal<string | null>(null);
  readonly #team = signal<PayrollTeam | null>(null);
  readonly #teamLoading = signal(false);
  readonly teamRuns = this.#teamRuns.asReadonly();
  readonly teamRunsLoading = this.#teamRunsLoading.asReadonly();
  readonly teamId = this.#teamId.asReadonly();
  readonly team = this.#team.asReadonly();
  readonly teamLoading = this.#teamLoading.asReadonly();

  private currentId: string | null = null;

  /** Whether the server offers the signed-in person this action on the open run. */
  can(action: PayrollRunAction): boolean {
    return this.#detail()?.allowedActions.includes(action) ?? false;
  }

  // ── list actions ──────────────────────────────────────────────────────────────

  loadRuns(): void {
    this.#runsLoading.set(true);
    this.svc.list().subscribe({
      next: (rows) => {
        this.#runs.set(rows);
        this.#runsLoading.set(false);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load the payroll runs.');
        this.#runsLoading.set(false);
      },
    });
  }

  /** Starts a month's draft; emits the new run's id. */
  start(year: number, month: number): Observable<string> {
    return this.svc.start(year, month).pipe(
      map((r) => r.id),
      tap(() => this.toast.success('Draft started.')),
      this.refused(),
    );
  }

  // ── one run: reading ──────────────────────────────────────────────────────────

  /** Opens a run (the page calls this once; the store reads it again after every action). */
  open(id: string): void {
    if (this.currentId !== id) this.#detail.set(null);
    this.currentId = id;
    this.reload();
  }

  /** Forgets the open run, so the next page never starts from someone else's figures. */
  close(): void {
    this.currentId = null;
    this.#detail.set(null);
  }

  reload(): void {
    const id = this.currentId;
    if (!id) return;
    this.#detailLoading.set(this.#detail() === null);
    this.svc.get(id, true).subscribe({
      next: (d) => {
        if (id !== this.currentId) return; // an answer for a run the person already left
        this.#detail.set(d);
        this.#detailLoading.set(false);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load the payroll run.');
        this.#detailLoading.set(false);
      },
    });
  }

  // ── one run: actions ──────────────────────────────────────────────────────────

  recalculate(): Observable<unknown> {
    return this.act(
      () => this.svc.recalculate(this.id(), this.version()),
      'Everyone has been calculated again.',
    );
  }

  updateLine(employeeId: string, edit: RunLineEdit): Observable<unknown> {
    return this.act(
      () =>
        this.svc.updateLine(this.id(), employeeId, { ...edit, expectedVersion: this.version() }),
      'Saved.',
    );
  }

  submit(): Observable<unknown> {
    return this.act(() => this.svc.submit(this.id(), this.version()), 'Submitted for sign-off.');
  }

  /** Deletes the open draft. The run is gone, so after success it is forgotten, not read again (that would only say "not found"). */
  remove(): Observable<unknown> {
    return this.act(() => this.svc.delete(this.id()), 'Draft run deleted.', {
      onDone: () => this.close(),
    });
  }

  signOff(l: PayrollRunLine): Observable<unknown> {
    return this.act(
      () => this.svc.signOff(this.id(), l.employeeId, this.version()),
      `Signed off ${l.name}.`,
    );
  }

  signOffAll(): Observable<unknown> {
    return this.act(
      () => this.svc.signOffAll(this.id(), this.version()),
      (r) => {
        const signed = (r as { signed: number }).signed;
        return signed > 0
          ? `Signed off ${signed} ${signed === 1 ? 'line' : 'lines'}.`
          : 'Nothing was left for you to sign off.';
      },
    );
  }

  approve(): Observable<unknown> {
    return this.act(() => this.svc.approve(this.id(), this.version()), 'Approved.');
  }

  returnRun(reason: string): Observable<unknown> {
    return this.act(
      () => this.svc.returnRun(this.id(), reason, this.version()),
      'Returned for changes.',
    );
  }

  lock(): Observable<unknown> {
    return this.act(() => this.svc.lock(this.id(), this.version()), 'Locked.');
  }

  reopen(): Observable<unknown> {
    return this.act(() => this.svc.reopen(this.id(), this.version()), 'Reopened as a draft.');
  }

  // ── a manager's view ──────────────────────────────────────────────────────────

  loadTeamRuns(): void {
    this.svc.teamRuns().subscribe({
      next: (rows) => {
        this.#teamRuns.set(rows);
        this.#teamRunsLoading.set(false);
        // Open what is waiting for this manager first, otherwise the newest run (only on the first load).
        if (this.#teamId() === null) {
          const first = rows.find((r) => r.toSign > 0) ?? rows[0];
          if (first) this.selectTeamRun(first.runId);
        }
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load your team.');
        this.#teamRunsLoading.set(false);
      },
    });
  }

  selectTeamRun(runId: string): void {
    if (runId !== this.#teamId()) this.#team.set(null); // never show one month under another month's tab
    this.#teamId.set(runId);
    this.reloadTeam();
  }

  private reloadTeam(): void {
    const id = this.#teamId();
    if (!id) return;
    this.#teamLoading.set(this.#team() === null);
    this.svc.team(id).subscribe({
      next: (t) => {
        // A slower answer for a tab the manager already left must not replace the one they are looking at.
        if (t.runId !== this.#teamId()) return;
        this.#team.set(t);
        this.#teamLoading.set(false);
      },
      error: (e: unknown) => {
        this.toast.error(apiErrorMessage(e) ?? 'Failed to load your team.');
        this.#teamLoading.set(false);
      },
    });
  }

  signOffTeamLine(l: PayrollRunLine): Observable<unknown> {
    const t = this.#team();
    if (!t || this.#busy()) return EMPTY;
    return this.act(
      () => this.svc.signOff(t.runId, l.employeeId, t.version),
      `Signed off ${l.name}.`,
      {
        onDone: () => {
          this.reloadTeam();
          // The tab badges count what is still waiting, so they follow a sign-off.
          this.svc
            .teamRuns()
            .subscribe({ next: (rows) => this.#teamRuns.set(rows), error: () => undefined });
        },
        onRefused: () => this.reloadTeam(),
      },
    );
  }

  // ── plumbing ──────────────────────────────────────────────────────────────────

  private id(): string {
    return this.currentId ?? '';
  }

  private version(): number {
    return this.#detail()?.summary.version ?? 0;
  }

  /** Says why a call was refused and completes empty, so a caller never needs an error callback. */
  private refused<T>() {
    return catchError<T, Observable<never>>((e: unknown) => {
      this.toast.error(apiErrorMessage(e) ?? 'That did not work.');
      return EMPTY;
    });
  }

  /**
   * Runs one action on the open run: one at a time, a toast on success, then the run is read again. On a refusal (a stale page, a
   * rule) the reason is shown and the run is read again so the page is current.
   */
  private act<T>(
    call: () => Observable<T>,
    success: string | ((r: T) => string),
    then: { onDone?: () => void; onRefused?: () => void } = {},
  ): Observable<T> {
    return new Observable<T>((out) => {
      if (this.#busy()) {
        out.complete();
        return undefined;
      }
      this.#busy.set(true);
      return call().subscribe({
        next: (r) => {
          this.toast.success(typeof success === 'function' ? success(r) : success);
          this.#busy.set(false);
          (then.onDone ?? (() => this.reload()))();
          out.next(r);
          out.complete();
        },
        error: (e: unknown) => {
          this.toast.error(apiErrorMessage(e) ?? 'That did not work.');
          this.#busy.set(false);
          (then.onRefused ?? (() => this.reload()))();
          out.complete();
        },
      });
    });
  }
}
