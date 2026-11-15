import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { PayrollRunService } from './payroll-run.service';

/**
 * WHY these tests matter: every step of a payroll run is an HTTP call that must go to the right place with the version the
 * page read. A call that drops the version, or sends the wrong body, would let a stale page overwrite someone's change.
 */
describe('PayrollRunService', () => {
  let service: PayrollRunService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PayrollRunService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PayrollRunService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() GETs the runs', () => {
    service.list().subscribe();
    const req = http.expectOne('/api/payroll/runs');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('get() asks for the not-in-run list only when told to', () => {
    service.get('r1').subscribe();
    http.expectOne((r) => r.url === '/api/payroll/runs/r1' && !r.params.has('notInRun')).flush({});

    service.get('r1', true).subscribe();
    const req = http.expectOne(
      (r) => r.url === '/api/payroll/runs/r1' && r.params.get('notInRun') === 'true',
    );
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('start() POSTs the year and month', () => {
    service.start(2026, 10).subscribe();
    const req = http.expectOne('/api/payroll/runs');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ year: 2026, month: 10 });
    req.flush({ id: 'r1' });
  });

  it('delete() DELETEs the run', () => {
    service.delete('r1').subscribe();
    const req = http.expectOne('/api/payroll/runs/r1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('updateLine() PUTs the inputs with the version the page read', () => {
    service
      .updateLine('r1', 'e1', {
        pcb: 120,
        excluded: false,
        excludedReason: null,
        expectedVersion: 7,
      })
      .subscribe();
    const req = http.expectOne('/api/payroll/runs/r1/lines/e1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      pcb: 120,
      excluded: false,
      excludedReason: null,
      expectedVersion: 7,
    });
    req.flush({ version: 8 });
  });

  it.each([
    ['recalculate', 'recalculate'],
    ['submit', 'submit'],
    ['approve', 'approve'],
  ] as const)('%s() POSTs the expected version to /%s', (method, path) => {
    service[method]('r1', 5).subscribe();
    const req = http.expectOne(`/api/payroll/runs/r1/${path}`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ expectedVersion: 5 });
    req.flush({ version: 6 });
  });

  it('returnRun() sends the reason and the version', () => {
    service.returnRun('r1', 'PCB missing', 5).subscribe();
    const req = http.expectOne('/api/payroll/runs/r1/return');
    expect(req.request.body).toEqual({ reason: 'PCB missing', expectedVersion: 5 });
    req.flush({ version: 6 });
  });

  it('signOff() and signOffAll() send the version', () => {
    service.signOff('r1', 'e1', 9).subscribe();
    const one = http.expectOne('/api/payroll/runs/r1/lines/e1/signoff');
    expect(one.request.body).toEqual({ expectedVersion: 9 });
    one.flush({ version: 10 });

    service.signOffAll('r1', 10).subscribe();
    const all = http.expectOne('/api/payroll/runs/r1/signoff-all');
    expect(all.request.body).toEqual({ expectedVersion: 10 });
    all.flush({ signed: 3 });
  });

  it('lock() and reopen() POST to their own routes', () => {
    service.lock('r1').subscribe();
    http.expectOne('/api/payroll/runs/r1/lock').flush(null);
    service.reopen('r1').subscribe();
    http.expectOne('/api/payroll/runs/r1/reopen').flush(null);
  });

  it('teamRuns() and team() read the manager views', () => {
    service.teamRuns().subscribe();
    http.expectOne('/api/payroll/runs/team').flush([]);
    service.team('r1').subscribe();
    const req = http.expectOne('/api/payroll/runs/r1/team');
    expect(req.request.method).toBe('GET');
    req.flush({ lines: [] });
  });
});
