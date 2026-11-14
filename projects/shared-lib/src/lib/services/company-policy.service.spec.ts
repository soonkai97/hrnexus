import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { CompanyPolicy, CompanyPolicyService, DEFAULT_POLICY } from './company-policy.service';

/**
 * WHY these tests matter: every leave form previews hours with the company's working week. If the
 * policy failed to load and the preview broke, staff could not apply for leave at all, so the service
 * must fall back to the shipped defaults. And a save from a stale page must never silently overwrite
 * a colleague's change, so the loaded version has to travel back with the save.
 */
describe('CompanyPolicyService', () => {
  let service: CompanyPolicyService;
  let http: HttpTestingController;

  const monSat: CompanyPolicy = {
    workingDays: [1, 2, 3, 4, 5, 6],
    workdayHours: 8,
    annualLeaveAdvanceDays: 3,
    maxLeaveDays: 30,
    carryOverMode: 'unlimited',
    carryOverMaxHours: 0,
    unpaidLeaveDivisor: 26,
    version: 4,
  };

  /** The editable values of the policy (everything but the version the service attaches itself). */
  const edit = (changes: Partial<CompanyPolicy>): Omit<CompanyPolicy, 'version'> => {
    const values: Partial<CompanyPolicy> = { ...monSat, ...changes };
    delete values.version;
    return values as Omit<CompanyPolicy, 'version'>;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CompanyPolicyService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CompanyPolicyService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts on the shipped defaults (Monday to Friday, 8 hours) before anything loads', () => {
    expect(service.policy()).toEqual(DEFAULT_POLICY);
    expect(service.schedule()).toEqual({ days: [1, 2, 3, 4, 5], hours: 8 });
    expect(service.loaded()).toBe(false);
  });

  it('load() replaces the defaults with the company policy and derives the schedule', () => {
    service.load().subscribe();
    http.expectOne('/api/company-policy').flush(monSat);

    expect(service.policy()).toEqual(monSat);
    expect(service.schedule()).toEqual({ days: [1, 2, 3, 4, 5, 6], hours: 8 });
    expect(service.loaded()).toBe(true);
  });

  it('keeps the defaults when the policy cannot be loaded, so leave forms still work', () => {
    let finished = false;
    service.load().subscribe({ complete: () => (finished = true) });
    http
      .expectOne('/api/company-policy')
      .flush('boom', { status: 500, statusText: 'Server Error' });

    expect(finished).toBe(true);
    expect(service.policy()).toEqual(DEFAULT_POLICY);
    expect(service.loaded()).toBe(true);
  });

  it('save() sends the edited values with the version that was loaded', () => {
    service.load().subscribe();
    http.expectOne('/api/company-policy').flush(monSat);

    const edited = edit({ workdayHours: 6 });
    service.save(edited).subscribe();

    const req = http.expectOne('/api/company-policy');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ ...edited, version: 4 });
    req.flush({ version: 5 });
  });

  it('after a save the service holds the new values and the new version', () => {
    service.load().subscribe();
    http.expectOne('/api/company-policy').flush(monSat);

    const edited = edit({ workingDays: [1, 2, 3, 4, 5], workdayHours: 7 });
    service.save(edited).subscribe();
    http.expectOne('/api/company-policy').flush({ version: 5 });

    expect(service.policy()).toEqual({ ...edited, version: 5 });
    expect(service.schedule()).toEqual({ days: [1, 2, 3, 4, 5], hours: 7 });
  });

  it('a refused save (stale version) leaves the held policy untouched and reports the error', () => {
    service.load().subscribe();
    http.expectOne('/api/company-policy').flush(monSat);

    let status = 0;
    const edited = edit({ workdayHours: 6 });
    service.save(edited).subscribe({ error: (e) => (status = e.status) });
    http
      .expectOne('/api/company-policy')
      .flush({ error: 'changed by someone else' }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
    expect(service.policy()).toEqual(monSat);
  });
});
