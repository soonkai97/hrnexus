import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { tap } from 'rxjs';
import { Location } from '../models/location.model';

export interface UpsertLocationDto {
  name: string;
}

@Injectable({ providedIn: 'root' })
export class LocationStore {
  private readonly http = inject(HttpClient);

  readonly #locations = signal<Location[]>([]);
  readonly #loading   = signal(false);
  readonly #error     = signal('');

  readonly locations = this.#locations.asReadonly();
  readonly loading   = this.#loading.asReadonly();
  readonly error     = this.#error.asReadonly();
  readonly count     = computed(() => this.#locations().length);

  // The signed-in staff member's assigned locations (the only ones they may log shifts at).
  readonly #myLocations = signal<Location[]>([]);
  readonly #myLoaded    = signal(false);
  readonly myLocations  = this.#myLocations.asReadonly();
  readonly myLoaded     = this.#myLoaded.asReadonly();

  loadMine(): void {
    this.http.get<Location[]>('/api/locations/mine').subscribe({
      next: list => { this.#myLocations.set(list); this.#myLoaded.set(true); },
      error: ()   => { this.#myLoaded.set(true); },
    });
  }

  load(): void {
    if (this.#loading()) return; // dedup: skip if a fetch is already in flight
    this.#loading.set(true);
    this.http.get<Location[]>('/api/locations').subscribe({
      next: list => { this.#locations.set(list); this.#loading.set(false); },
      error: ()   => { this.#error.set('Failed to load locations.'); this.#loading.set(false); },
    });
  }

  create(dto: UpsertLocationDto) {
    return this.http.post<{ id: string }>('/api/locations', dto).pipe(
      tap(() => this.load())
    );
  }

  /** Create many locations at once from a list of names (duplicates skipped server-side). */
  bulkCreate(names: string[]) {
    return this.http.post<{ created: number; skipped: number }>('/api/locations/bulk', { names }).pipe(
      tap(() => this.load())
    );
  }

  update(id: string, dto: UpsertLocationDto) {
    return this.http.put<void>(`/api/locations/${id}`, dto).pipe(
      tap(() => this.#locations.update(list =>
        list.map(l => l.id === id ? { ...l, ...dto } : l)
      ))
    );
  }

  remove(id: string) {
    return this.http.delete<void>(`/api/locations/${id}`).pipe(
      tap(() => this.#locations.update(list => list.filter(l => l.id !== id)))
    );
  }

  uploadImage(id: string, file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ path: string }>(`/api/locations/${id}/image`, form).pipe(
      tap(res => this.#locations.update(list =>
        list.map(l => l.id === id ? { ...l, imagePath: res.path } : l)
      ))
    );
  }
}
