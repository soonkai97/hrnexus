import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

/** Build metadata baked into the deployed app (public/version.json). */
export interface BuildInfo {
  /** Human-facing build number, e.g. "20260704.01" (date + Nth build that day). */
  version: string;
  /** Short git commit SHA of the build — traceability only, not shown prominently. */
  commit?: string;
  /** ISO timestamp the artifact was built. */
  builtAt?: string;
}

/**
 * Exposes the current front-end build version, read once from the static
 * /version.json that the Docker build stamps into each app. Fetched at runtime
 * (never bundled) so it matches the venue's "config lives at the edge" pattern
 * and always reflects the artifact nginx is actually serving.
 */
@Injectable({ providedIn: 'root' })
export class VersionService {
  private readonly http = inject(HttpClient);

  /** Empty until load() resolves; 'dev' locally where no build stamp exists. */
  readonly info = signal<BuildInfo>({ version: 'dev' });

  private loaded = false;

  /** Idempotent — safe to call from every page that shows the version. */
  load(): void {
    if (this.loaded) return;
    this.loaded = true;
    // Cache-bust so a redeploy is reflected even if the service worker cached the old file.
    this.http.get<BuildInfo>('/version.json', { params: { t: `${Date.now()}` } })
      .subscribe({
        next: info => { if (info?.version) this.info.set(info); },
        error: () => { /* offline / dev — keep the 'dev' fallback */ },
      });
  }
}
