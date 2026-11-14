import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { LeaveTypeService, DEFAULT_LEAVE_TYPES } from './leave-type.service';
import { LeaveTypeCatalogue, LeaveTypeDefinition } from '../models/leave.model';

/**
 * WHY these tests matter: the leave forms, tags, balances and notice rules are all built from the company's
 * leave types. If the catalogue failed to load and the forms broke, staff could not apply at all, so the
 * service must fall back to the built-in types; and a type a company turned off must stop being offered
 * while its old leave keeps its name. The settings page's save must carry the loaded version so a stale
 * page never overwrites a colleague's change.
 */
describe('LeaveTypeService', () => {
  let service: LeaveTypeService;
  let http: HttpTestingController;

  const type = (changes: Partial<LeaveTypeDefinition>): LeaveTypeDefinition => ({
    key: 'study',
    label: 'Study',
    colour: 'info',
    sortOrder: 100,
    active: true,
    isSystem: false,
    pool: 'none',
    allowanceDays: null,
    maxDaysPerRequest: null,
    autoApprove: false,
    advanceNoticeDays: null,
    advanceNoticeFromPolicy: false,
    deductsPay: false,
    adminOnly: false,
    countsCalendarDays: false,
    sensitive: false,
    hasLeave: false,
    ...changes,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LeaveTypeService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LeaveTypeService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── defaults ──────────────────────────────────────────────────────────────────

  it('starts on the built-in types, so the forms work before anything loads', () => {
    expect(service.types().map((t) => t.key)).toEqual([
      'annual',
      'emergency',
      'sick',
      'hospitalisation',
      'maternity',
      'paternity',
      'compassionate',
      'unpaid',
      'no-show',
    ]);
    expect(service.loaded()).toBe(false);
  });

  it('the built-in maternity type counts calendar days with its own 98-day limit and is not announced by name', () => {
    const m = DEFAULT_LEAVE_TYPES.find((t) => t.key === 'maternity')!;
    expect(m.countsCalendarDays).toBe(true);
    expect(m.maxDaysPerRequest).toBe(98);
    expect(m.sensitive).toBe(true);
    expect(m.autoApprove).toBe(false);
  });

  // ── loading ───────────────────────────────────────────────────────────────────

  it('load() replaces the built-ins with the saved catalogue, in sort order', () => {
    service.load().subscribe();
    http.expectOne('/api/leave-types').flush({
      version: 2,
      isSaved: true,
      types: [type({ key: 'b', label: 'B', sortOrder: 20 }), type({ key: 'a', label: 'A', sortOrder: 10 })],
    } satisfies LeaveTypeCatalogue);

    expect(service.types().map((t) => t.key)).toEqual(['a', 'b']);
    expect(service.loaded()).toBe(true);
  });

  it('keeps the built-ins when the catalogue cannot be loaded, so staff can still apply', () => {
    let finished = false;
    service.load().subscribe(() => (finished = true));
    http.expectOne('/api/leave-types').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(finished).toBe(true);
    expect(service.types().length).toBe(DEFAULT_LEAVE_TYPES.length);
    expect(service.loaded()).toBe(true);
  });

  // ── what staff can pick ───────────────────────────────────────────────────────

  it('offers staff only active types that are not admin-only', () => {
    service.load().subscribe();
    http.expectOne('/api/leave-types').flush({
      version: 1,
      isSaved: true,
      types: [
        type({ key: 'open', label: 'Open', sortOrder: 1 }),
        type({ key: 'off', label: 'Off', sortOrder: 2, active: false }),
        type({ key: 'admin', label: 'Admin', sortOrder: 3, adminOnly: true }),
      ],
    });

    expect(service.applicable().map((t) => t.key)).toEqual(['open']);
  });

  it('a deactivated type keeps its name for old leave but is not offered', () => {
    service.load().subscribe();
    http.expectOne('/api/leave-types').flush({
      version: 1,
      isSaved: true,
      types: [type({ key: 'old', label: 'Old scheme', active: false })],
    });

    expect(service.label('old')).toBe('Old scheme');
    expect(service.applicable()).toEqual([]);
  });

  it('shows an unknown key as it was stored, and a neutral colour', () => {
    expect(service.label('mystery')).toBe('mystery');
    expect(service.severity('mystery')).toBe('secondary');
  });

  it('takes the tag colour from the type', () => {
    expect(service.severity('sick')).toBe('danger');
    expect(service.severity('annual')).toBe('success');
  });

  // ── the rules a form needs ────────────────────────────────────────────────────

  it('a calendar-day type counts every day of the week at the company hours per day', () => {
    const company = { days: [1, 2, 3, 4, 5], hours: 7.5 };

    expect(service.scheduleFor('maternity', company)).toEqual({ days: [1, 2, 3, 4, 5, 6, 7], hours: 7.5 });
    expect(service.scheduleFor('annual', company)).toBe(company);
    expect(service.scheduleFor('mystery', company)).toBe(company);
  });

  it('notice comes from the company policy for a type that says so, from its own days otherwise, and is none for the rest', () => {
    const own = type({ key: 'own', advanceNoticeDays: 10 });
    const fromPolicy = type({ key: 'fp', advanceNoticeFromPolicy: true });

    expect(service.noticeDays(fromPolicy, 4)).toBe(4);
    expect(service.noticeDays(own, 4)).toBe(10);
    expect(service.noticeDays(type({}), 4)).toBeNull();
  });

  it('a type is the longest it allows in calendar days, or the policy cap when it has none', () => {
    expect(service.maxDays('maternity', 30)).toBe(98);
    expect(service.maxDays('annual', 30)).toBe(30);
    expect(service.maxDays('mystery', 30)).toBe(30);
  });

  // ── settings page ─────────────────────────────────────────────────────────────

  it('loadAll() returns every type with which already have leave, and the version to save against', () => {
    let got: LeaveTypeCatalogue | undefined;
    service.loadAll().subscribe((c) => (got = c));
    http.expectOne('/api/leave-types/all').flush({ version: 3, isSaved: true, types: [type({ hasLeave: true })] });

    expect(got?.version).toBe(3);
    expect(got?.types[0]?.hasLeave).toBe(true);
  });

  it('save() sends the whole catalogue with the loaded version and refreshes what the forms offer', () => {
    const types = [type({ key: 'study', label: 'Study' })];
    let version: number | undefined;

    service.save(3, types).subscribe((v) => (version = v));
    const put = http.expectOne('/api/leave-types');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.version).toBe(3);
    expect(put.request.body.types.map((t: LeaveTypeDefinition) => t.key)).toEqual(['study']);
    put.flush({ version: 4 });
    // The forms are rebuilt from the server's answer.
    http.expectOne('/api/leave-types').flush({ version: 4, isSaved: true, types });

    expect(version).toBe(4);
    expect(service.types().map((t) => t.key)).toEqual(['study']);
  });

  it('save() lets a stale-version refusal reach the caller', () => {
    let status: number | undefined;
    service.save(1, [type({})]).subscribe({ error: (e) => (status = e.status) });
    http.expectOne('/api/leave-types').flush({ error: 'someone else' }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
  });
});
