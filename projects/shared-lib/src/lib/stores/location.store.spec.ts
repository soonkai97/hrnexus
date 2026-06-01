import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { LocationStore } from './location.store';
import { Location } from '../models/location.model';

function makeLocation(id: string, overrides: Partial<Location> = {}): Location {
  return { id, name: `Location ${id}`, imagePath: null, ...overrides };
}

/**
 * LocationStore — signal-based state for venue location list, loading, and error states.
 *
 * WHY these tests matter:
 *  - locations() is the source of truth rendered in the Locations admin page and
 *    the Staff form location picker; stale or incorrectly merged state corrupts
 *    both UIs simultaneously.
 *  - load() must set loading=true before the request so the skeleton UX fires,
 *    and must clear loading on both success and error to avoid stuck spinners.
 *  - update() applies an optimistic in-place patch so the UI reflects the edit
 *    immediately without a round-trip reload.
 *  - remove() filters the item out of the signal immediately after DELETE so
 *    the list shrinks without a full re-fetch.
 *  - uploadImage() updates only the imagePath of the target location so other
 *    fields are not accidentally reset.
 *  - count is a derived signal — it must always equal locations().length.
 */
describe('LocationStore', () => {
  let store: LocationStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(LocationStore);
    http  = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── Initial state ──────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with empty locations array', () => {
      expect(store.locations()).toEqual([]);
    });

    it('starts with loading=false so the page does not show a spinner before load() is called', () => {
      expect(store.loading()).toBe(false);
    });

    it('starts with error empty string', () => {
      expect(store.error()).toBe('');
    });

    it('starts with count=0 matching empty locations', () => {
      expect(store.count()).toBe(0);
    });
  });

  // ── load() — success path ─────────────────────────────────────────────────

  describe('load()', () => {
    it('calls GET /api/locations', () => {
      store.load();
      const req = http.expectOne('/api/locations');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('sets loading=true immediately so the skeleton UX renders before the response', () => {
      store.load();
      expect(store.loading()).toBe(true);
      http.expectOne('/api/locations').flush([]);
    });

    it('populates the locations signal from the API response', () => {
      const locs = [makeLocation('loc-1'), makeLocation('loc-2')];
      store.load();
      http.expectOne('/api/locations').flush(locs);
      expect(store.locations()).toEqual(locs);
    });

    it('sets loading=false after the response arrives', () => {
      store.load();
      http.expectOne('/api/locations').flush([]);
      expect(store.loading()).toBe(false);
    });

    it('updates count to match the number of locations returned', () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('a'), makeLocation('b'), makeLocation('c')]);
      expect(store.count()).toBe(3);
    });
  });

  // ── load() — error path ───────────────────────────────────────────────────

  describe('load() — error path', () => {
    it('sets error signal on API failure', () => {
      store.load();
      http.expectOne('/api/locations').error(new ErrorEvent('Network error'));
      expect(store.error()).toBe('Failed to load locations.');
    });

    it('clears loading=false on API failure so the spinner does not get stuck', () => {
      store.load();
      http.expectOne('/api/locations').error(new ErrorEvent('Network error'));
      expect(store.loading()).toBe(false);
    });

    it('does not wipe existing locations on a failed refresh', () => {
      // First load succeeds
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1')]);
      expect(store.locations().length).toBe(1);

      // Second load fails — existing data must be preserved
      store.load();
      http.expectOne('/api/locations').error(new ErrorEvent('Network error'));
      expect(store.locations().length).toBe(1);
    });
  });

  // ── create() ──────────────────────────────────────────────────────────────

  describe('create()', () => {
    it('calls POST /api/locations with the DTO body', async () => {
      const dto = { name: 'VIP Room' };
      const promise = firstValueFrom(store.create(dto));
      const postReq = http.expectOne('/api/locations');
      expect(postReq.request.method).toBe('POST');
      expect(postReq.request.body).toEqual(dto);
      postReq.flush({ id: 'new-loc-id' });
      // create() taps load() — flush that reload too
      http.expectOne('/api/locations').flush([makeLocation('new-loc-id', { name: 'VIP Room' })]);
      await promise;
    });

    it('triggers a reload after creation so the list reflects the new record', async () => {
      const promise = firstValueFrom(store.create({ name: 'Bar Area' }));
      http.expectOne(r => r.url === '/api/locations' && r.method === 'POST').flush({ id: 'x' });
      // This second GET is the tap-triggered reload
      const reloadReq = http.expectOne(r => r.url === '/api/locations' && r.method === 'GET');
      reloadReq.flush([makeLocation('x', { name: 'Bar Area' })]);
      await promise;
      expect(store.locations().length).toBe(1);
    });
  });

  // ── update() ──────────────────────────────────────────────────────────────

  describe('update()', () => {
    it('calls PUT /api/locations/{id} with the DTO body', async () => {
      // Seed the store first
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1')]);

      const promise = firstValueFrom(store.update('loc-1', { name: 'Updated Name' }));
      const req = http.expectOne('/api/locations/loc-1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ name: 'Updated Name' });
      req.flush(null);
      await promise;
    });

    it('patches the name of the matching location in-place', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1'), makeLocation('loc-2')]);

      const promise = firstValueFrom(store.update('loc-1', { name: 'Renamed' }));
      http.expectOne('/api/locations/loc-1').flush(null);
      await promise;

      expect(store.locations().find(l => l.id === 'loc-1')?.name).toBe('Renamed');
    });

    it('does not modify other locations when patching one', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1'), makeLocation('loc-2')]);

      const promise = firstValueFrom(store.update('loc-1', { name: 'Changed' }));
      http.expectOne('/api/locations/loc-1').flush(null);
      await promise;

      expect(store.locations().find(l => l.id === 'loc-2')?.name).toBe('Location loc-2');
    });
  });

  // ── bulkCreate() ────────────────────────────────────────────────────────────

  describe('bulkCreate()', () => {
    it('POSTs /api/locations/bulk with the names and reloads', async () => {
      const promise = firstValueFrom(store.bulkCreate(['Main Bar', 'Rooftop']));
      const postReq = http.expectOne('/api/locations/bulk');
      expect(postReq.request.method).toBe('POST');
      expect(postReq.request.body).toEqual({ names: ['Main Bar', 'Rooftop'] });
      postReq.flush({ created: 2, skipped: 0 });
      // bulkCreate taps load() — flush the reload GET too
      http.expectOne('/api/locations').flush([]);
      await promise;
    });
  });

  // ── remove() ──────────────────────────────────────────────────────────────

  describe('remove()', () => {
    it('calls DELETE /api/locations/{id}', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1')]);

      const promise = firstValueFrom(store.remove('loc-1'));
      const req = http.expectOne('/api/locations/loc-1');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      await promise;
    });

    it('removes the deleted location from the signal', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1'), makeLocation('loc-2')]);

      const promise = firstValueFrom(store.remove('loc-1'));
      http.expectOne('/api/locations/loc-1').flush(null);
      await promise;

      expect(store.locations().find(l => l.id === 'loc-1')).toBeUndefined();
    });

    it('leaves other locations intact after removal', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1'), makeLocation('loc-2')]);

      const promise = firstValueFrom(store.remove('loc-1'));
      http.expectOne('/api/locations/loc-1').flush(null);
      await promise;

      expect(store.locations().find(l => l.id === 'loc-2')).toBeDefined();
    });

    it('decrements count after removal', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1'), makeLocation('loc-2')]);
      expect(store.count()).toBe(2);

      const promise = firstValueFrom(store.remove('loc-1'));
      http.expectOne('/api/locations/loc-1').flush(null);
      await promise;

      expect(store.count()).toBe(1);
    });
  });

  // ── uploadImage() ─────────────────────────────────────────────────────────

  describe('uploadImage()', () => {
    it('calls POST /api/locations/{id}/image with a FormData body containing the file', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1')]);

      const file = new File(['img'], 'photo.jpg', { type: 'image/jpeg' });
      const promise = firstValueFrom(store.uploadImage('loc-1', file));
      const req = http.expectOne('/api/locations/loc-1/image');
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBe(true);
      req.flush({ path: '/uploads/photo.jpg' });
      await promise;
    });

    it('updates the imagePath of the matching location after upload', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1')]);

      const file = new File(['img'], 'photo.jpg', { type: 'image/jpeg' });
      const promise = firstValueFrom(store.uploadImage('loc-1', file));
      http.expectOne('/api/locations/loc-1/image').flush({ path: '/uploads/photo.jpg' });
      await promise;

      expect(store.locations().find(l => l.id === 'loc-1')?.imagePath).toBe('/uploads/photo.jpg');
    });

    it('does not modify imagePath of other locations', async () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('loc-1'), makeLocation('loc-2')]);

      const file = new File(['img'], 'photo.jpg', { type: 'image/jpeg' });
      const promise = firstValueFrom(store.uploadImage('loc-1', file));
      http.expectOne('/api/locations/loc-1/image').flush({ path: '/uploads/photo.jpg' });
      await promise;

      expect(store.locations().find(l => l.id === 'loc-2')?.imagePath).toBeNull();
    });
  });

  // ── count (computed) ──────────────────────────────────────────────────────

  describe('count (computed)', () => {
    it('always equals locations().length', () => {
      store.load();
      http.expectOne('/api/locations').flush([makeLocation('a'), makeLocation('b')]);
      expect(store.count()).toBe(store.locations().length);
    });
  });
});
