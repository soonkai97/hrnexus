import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { StaffStore } from './staff.store';
import { Staff } from '../models';

function makeStaff(id: string, overrides: Partial<Staff> = {}): Staff {
  return {
    id,
    staffId: `NC00${id}`,
    firstName: 'First',
    lastName: 'Last',
    fullName: 'First Last',
    email: `${id}@test.com`,
    roles: ['employee'],
    isActive: true,
    phoneNumber: null,
    icNo: null,
    passportNo: null,
    dateOfBirth: null,
    age: null,
    ...overrides,
  };
}

/**
 * StaffStore — signal-based state for staff list, loading, and error states.
 *
 * WHY these tests matter:
 *  - The store is the single source of truth for staff data across admin features
 *    (list, detail, payroll). Incorrect signal updates cause stale UI renders.
 *  - loadStaff() must set loading=true before the request so skeleton UX fires;
 *    it must clear loading on both success and error.
 *  - update() applies an optimistic in-place patch so the UI reflects changes
 *    immediately without a full re-fetch.
 *  - toggleActive() flips a single staff member's isActive without touching others.
 */
describe('StaffStore', () => {
  let store: StaffStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(StaffStore);
    http  = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── Initial state ──────────────────────────────────────────────────────────

  it('starts with empty staff array, loading=false, totalCount=0', () => {
    expect(store.staff()).toEqual([]);
    expect(store.loading()).toBe(false);
    expect(store.totalCount()).toBe(0);
    expect(store.error()).toBe('');
  });

  // ── loadStaff() — success path ─────────────────────────────────────────────

  it('sets loading=true immediately when loadStaff() is called', () => {
    store.loadStaff();
    // Before HTTP completes, loading must be true so skeleton renders
    expect(store.loading()).toBe(true);
    // Clean up the pending request
    http.expectOne(r => r.url.includes('/api/staff')).flush({ items: [], total: 0 });
  });

  it('populates staff signal and totalCount from API response', () => {
    const items = [makeStaff('1'), makeStaff('2')];
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff')).flush({ items, total: 2 });

    expect(store.staff()).toEqual(items);
    expect(store.totalCount()).toBe(2);
  });

  it('sets loading=false after successful response', () => {
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff')).flush({ items: [], total: 0 });
    expect(store.loading()).toBe(false);
  });

  it('sends page and pageSize as query params', () => {
    store.loadStaff(undefined, 2, 5);
    const req = http.expectOne(r => r.url.includes('/api/staff'));
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('5');
    req.flush({ items: [], total: 0 });
  });

  it('includes search param when a search term is provided', () => {
    store.loadStaff('alice');
    const req = http.expectOne(r => r.url.includes('/api/staff'));
    expect(req.request.params.get('search')).toBe('alice');
    req.flush({ items: [], total: 0 });
  });

  // ── loadStaff() — error path ───────────────────────────────────────────────

  it('sets error signal and clears loading on API failure', () => {
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff')).error(new ErrorEvent('Network error'));

    expect(store.loading()).toBe(false);
    expect(store.error()).toBe('Failed to load staff.');
  });

  it('does not wipe existing staff on a failed refresh', () => {
    // First load succeeds
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff'))
      .flush({ items: [makeStaff('1')], total: 1 });
    expect(store.staff().length).toBe(1);

    // Second load fails — staff should remain
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff')).error(new ErrorEvent('Network error'));
    expect(store.staff().length).toBe(1);
  });

  // ── update() ──────────────────────────────────────────────────────────────

  it('patches the matching staff member in-place after update() completes', async () => {
    // Seed the store
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff'))
      .flush({ items: [makeStaff('1'), makeStaff('2')], total: 2 });

    const req$ = store.update('1', {
      staffId: 'NC001', firstName: 'Updated', lastName: 'Name',
      email: '1@test.com', roles: ['manager'],
      phoneNumber: null, icNo: null, passportNo: null, dateOfBirth: null,
    });
    const promise = firstValueFrom(req$);
    http.expectOne(r => r.url.includes('/api/staff/1') && r.method === 'PUT').flush(null);
    await promise;

    const updated = store.staff().find(s => s.id === '1')!;
    expect(updated.firstName).toBe('Updated');
    // Other staff members should be untouched
    expect(store.staff().find(s => s.id === '2')?.firstName).toBe('First');
  });

  // ── toggleActive() ────────────────────────────────────────────────────────

  it('flips isActive for the target staff member only', async () => {
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff'))
      .flush({ items: [makeStaff('1'), makeStaff('2', { isActive: false })], total: 2 });

    const req$ = store.toggleActive('2', false); // currently inactive → activate
    const promise = firstValueFrom(req$);
    http.expectOne(r => r.url.includes('/api/staff/2/activate')).flush(null);
    await promise;

    expect(store.staff().find(s => s.id === '2')?.isActive).toBe(true);
    // Staff '1' should be unchanged
    expect(store.staff().find(s => s.id === '1')?.isActive).toBe(true);
  });

  it('sends a deactivate request when currentlyActive=true', async () => {
    store.loadStaff();
    http.expectOne(r => r.url.includes('/api/staff'))
      .flush({ items: [makeStaff('1')], total: 1 });

    const promise = firstValueFrom(store.toggleActive('1', true));
    http.expectOne(r => r.url.includes('/api/staff/1/deactivate')).flush(null);
    await promise;

    expect(store.staff().find(s => s.id === '1')?.isActive).toBe(false);
  });

  // ── loadRoles() ───────────────────────────────────────────────────────────

  it('populates the roles signal from the API', () => {
    store.loadRoles();
    http.expectOne('/api/staff/roles').flush([
      { name: 'bartender', hourlyRate: 15 },
      { name: 'manager',   hourlyRate: 25 },
    ]);

    expect(store.roles().length).toBe(2);
    expect(store.roles()[0].name).toBe('bartender');
  });

  it('silently ignores role-load errors — roles are not critical for navigation', () => {
    store.loadRoles();
    http.expectOne('/api/staff/roles').error(new ErrorEvent('Network error'));
    expect(store.roles()).toEqual([]); // unchanged, no throw
  });

  // ── register() ────────────────────────────────────────────────────────────

  it('register() POST /api/staff and returns the new staff id', async () => {
    const req$ = store.register({
      staffId: 'NC010', firstName: 'New', lastName: 'Staff', email: 'n@test.com',
      password: 'pass', roles: ['employee'], phoneNumber: '', icNo: null, passportNo: null, dateOfBirth: null,
    });
    const promise = firstValueFrom(req$);
    const req = http.expectOne('/api/staff');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 'new-id' });
    const result = await promise;
    expect(result).toEqual({ id: 'new-id' });
  });

  // ── createRole() ──────────────────────────────────────────────────────────

  it('createRole() POST /api/staff/roles and triggers a roles reload', async () => {
    const promise = firstValueFrom(store.createRole('bouncer', 20));
    http.expectOne('/api/staff/roles').flush(null); // POST
    await promise;
    // createRole() calls loadRoles() via tap — consume that request
    http.expectOne('/api/staff/roles').flush([{ name: 'bouncer', isSystem: false, hourlyRate: 20 }]);
  });

  // ── updateRoleRate() ──────────────────────────────────────────────────────

  it('updateRoleRate() PATCH the role rate and updates the signal in-place', async () => {
    store.loadRoles();
    http.expectOne('/api/staff/roles').flush([
      { name: 'bartender', isSystem: false, hourlyRate: 15 },
    ]);

    const promise = firstValueFrom(store.updateRoleRate('bartender', 18));
    http.expectOne(r => r.url.includes('/api/staff/roles/bartender/rate')).flush(null);
    await promise;

    expect(store.roles().find(r => r.name === 'bartender')?.hourlyRate).toBe(18);
  });

  // ── deleteRole() ──────────────────────────────────────────────────────────

  it('deleteRole() DELETE the role and removes it from the signal', async () => {
    store.loadRoles();
    http.expectOne('/api/staff/roles').flush([
      { name: 'temp', isSystem: false, hourlyRate: 10 },
      { name: 'bartender', isSystem: false, hourlyRate: 15 },
    ]);

    const promise = firstValueFrom(store.deleteRole('temp'));
    http.expectOne(r => r.url.includes('/api/staff/roles/temp')).flush(null);
    await promise;

    expect(store.roles().find(r => r.name === 'temp')).toBeUndefined();
    expect(store.roles().find(r => r.name === 'bartender')).toBeDefined();
  });

  // ── resetPassword() ───────────────────────────────────────────────────────

  it('resetPassword() POST /api/staff/{id}/reset-password', async () => {
    const promise = firstValueFrom(store.resetPassword('staff-id-1', 'NewPass!'));
    const req = http.expectOne(r => r.url.includes('/api/staff/staff-id-1/reset-password'));
    expect(req.request.body).toEqual({ newPassword: 'NewPass!' });
    req.flush(null);
    await promise;
  });

  // ── deleteStaff() ─────────────────────────────────────────────────────────

  it('deleteStaff() DELETE /api/staff/{id}', async () => {
    const promise = firstValueFrom(store.deleteStaff('staff-id-2'));
    const req = http.expectOne(r => r.url.includes('/api/staff/staff-id-2') && r.method === 'DELETE');
    req.flush(null);
    await promise; // resolves without error
  });
});
