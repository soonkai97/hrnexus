import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  PayrollRunDetail,
  PayrollRunSummary,
  PayrollTeam,
  PayrollTeamRun,
  UpdateRunLineRequest,
} from '../models/payroll-run.model';

/**
 * The monthly payroll run. Reading needs payroll.view; each step needs its own permission on the server
 * (manage, approve, signoff, lock, reopen). Every action sends the run's `version` as it was read, so a page that
 * is out of date is refused (409) instead of overwriting someone else's change.
 */
@Injectable({ providedIn: 'root' })
export class PayrollRunService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payroll/runs';

  list(): Observable<PayrollRunSummary[]> {
    return this.http.get<PayrollRunSummary[]>(this.base);
  }

  /** One run in full. `notInRun` also asks the server who cannot be in a draft yet (it re-reads the month's inputs). */
  get(id: string, notInRun = false): Observable<PayrollRunDetail> {
    const params = notInRun ? new HttpParams().set('notInRun', 'true') : undefined;
    return this.http.get<PayrollRunDetail>(`${this.base}/${id}`, { params });
  }

  start(year: number, month: number): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.base, { year, month });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  recalculate(id: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.post<{ version: number }>(`${this.base}/${id}/recalculate`, {
      expectedVersion,
    });
  }

  updateLine(
    id: string,
    employeeId: string,
    body: UpdateRunLineRequest,
  ): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`${this.base}/${id}/lines/${employeeId}`, body);
  }

  submit(id: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.post<{ version: number }>(`${this.base}/${id}/submit`, { expectedVersion });
  }

  signOff(
    id: string,
    employeeId: string,
    expectedVersion: number,
  ): Observable<{ version: number }> {
    return this.http.post<{ version: number }>(`${this.base}/${id}/lines/${employeeId}/signoff`, {
      expectedVersion,
    });
  }

  signOffAll(id: string, expectedVersion: number): Observable<{ signed: number }> {
    return this.http.post<{ signed: number }>(`${this.base}/${id}/signoff-all`, {
      expectedVersion,
    });
  }

  returnRun(id: string, reason: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.post<{ version: number }>(`${this.base}/${id}/return`, {
      reason,
      expectedVersion,
    });
  }

  approve(id: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.post<{ version: number }>(`${this.base}/${id}/approve`, { expectedVersion });
  }

  lock(id: string, expectedVersion: number): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/lock`, { expectedVersion });
  }

  reopen(id: string, expectedVersion: number): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/reopen`, { expectedVersion });
  }

  /** Runs that have people who report to the signed-in manager (payroll.signoff only; no money). */
  teamRuns(): Observable<PayrollTeamRun[]> {
    return this.http.get<PayrollTeamRun[]>(`${this.base}/team`);
  }

  /** The signed-in manager's direct reports' lines in one run. */
  team(id: string): Observable<PayrollTeam> {
    return this.http.get<PayrollTeam>(`${this.base}/${id}/team`);
  }
}
