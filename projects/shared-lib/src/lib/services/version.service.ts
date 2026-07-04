import { Injectable, signal } from '@angular/core';

/** Build metadata baked into the deployed app. */
export interface BuildInfo {
  /** Human-facing build number, e.g. "20260704.05" (date + Nth build that day), or "dev" locally. */
  version: string;
}

/**
 * Exposes the current front-end build version.
 *
 * The version is baked into index.html at Docker-build time as
 * `<meta name="build-version" content="YYYYMMDD.NN">` and read here SYNCHRONOUSLY —
 * no runtime fetch. This matters because:
 *   - there's no network call to fail (a fetch that errored during a deploy swap
 *     used to latch the "dev" fallback for the whole session), and
 *   - it reflects the version of the app ACTUALLY RUNNING (the cached bundle the
 *     service worker served), not whatever the server happens to hold right now —
 *     which is exactly what "which build am I looking at?" should answer.
 */
@Injectable({ providedIn: 'root' })
export class VersionService {
  readonly info = signal<BuildInfo>({ version: this.readVersion() });

  private readVersion(): string {
    if (typeof document === 'undefined') return 'dev';
    const v = document.querySelector('meta[name="build-version"]')?.getAttribute('content')?.trim() ?? '';
    // Unreplaced placeholder (local `ng serve`, where the Docker sed never runs) → "dev".
    return v && !v.startsWith('__') ? v : 'dev';
  }

  /** No-op — the version is read at construction from the index.html meta. Kept so existing
   *  callers (account/profile pages call it in ngOnInit) don't need to change. */
  load(): void { /* intentionally empty */ }
}
