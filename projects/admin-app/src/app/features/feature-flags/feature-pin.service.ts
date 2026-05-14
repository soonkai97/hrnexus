import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, map } from 'rxjs';

const TOKEN_KEY   = 'nc_feature_token';
const EXPIRES_KEY = 'nc_feature_expires';

@Injectable({ providedIn: 'root' })
export class FeaturePinService {
  private readonly http = inject(HttpClient);

  isUnlocked(): boolean {
    const token   = sessionStorage.getItem(TOKEN_KEY);
    const expires = sessionStorage.getItem(EXPIRES_KEY);
    if (!token || !expires) return false;
    return new Date(expires) > new Date();
  }

  getToken(): string | null {
    return this.isUnlocked() ? sessionStorage.getItem(TOKEN_KEY) : null;
  }

  /** Returns remaining minutes until token expires, or 0 if not unlocked. */
  minutesRemaining(): number {
    const expires = sessionStorage.getItem(EXPIRES_KEY);
    if (!expires) return 0;
    return Math.max(0, Math.floor((new Date(expires).getTime() - Date.now()) / 60000));
  }

  unlock(pin: string): Observable<void> {
    return this.http
      .post<{ token: string; expiresAt: string }>('/api/feature-flags/unlock', { pin })
      .pipe(
        tap(res => {
          sessionStorage.setItem(TOKEN_KEY,   res.token);
          sessionStorage.setItem(EXPIRES_KEY, res.expiresAt);
        }),
        map(() => void 0),
      );
  }

  lock(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(EXPIRES_KEY);
  }
}
