import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface TwoFactorEnrollInfo {
  secret: string;
  otpAuthUri: string;
  qrPngDataUri: string;
}
export interface TwoFactorRecoveryInfo {
  recoveryCodes: string[];
}
export interface TwoFactorStatus {
  enabled: boolean;
}

/**
 * Manages a user's own two-factor (TOTP) enrollment. The login-time challenge/verify lives on
 * AuthService; this service is the account-settings side: begin enrollment (QR), confirm, disable,
 * and read current status.
 */
@Injectable({ providedIn: 'root' })
export class TwoFactorService {
  private readonly http = inject(HttpClient);

  status(): Observable<TwoFactorStatus> {
    return this.http.get<TwoFactorStatus>('/api/auth/2fa/status');
  }

  /** Generate a secret + QR; 2FA is not yet active until confirm() succeeds. */
  beginEnroll(): Observable<TwoFactorEnrollInfo> {
    return this.http.post<TwoFactorEnrollInfo>('/api/auth/2fa/enroll/begin', {});
  }

  /** Verify the first code to activate 2FA; returns the one-time recovery codes. */
  confirmEnroll(code: string): Observable<TwoFactorRecoveryInfo> {
    return this.http.post<TwoFactorRecoveryInfo>('/api/auth/2fa/enroll/confirm', { code });
  }

  /** Turn 2FA off — requires a current code (TOTP or recovery) to prove possession. */
  disable(code: string): Observable<void> {
    return this.http.post<void>('/api/auth/2fa/disable', { code });
  }
}
