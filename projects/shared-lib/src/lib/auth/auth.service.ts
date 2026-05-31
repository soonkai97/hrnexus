import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap, catchError, throwError } from 'rxjs';
import { AuthResponse, AuthUser, ChangePasswordRequest, ForgotPasswordResponse, LoginRequest, UserRole } from '../models';
import { PermissionService } from '../services/permission.service';

// Tokens now live in httpOnly cookies set by the server (the browser sends them automatically with
// withCredentials). JS never sees them, so XSS can't exfiltrate them. We still keep the NON-sensitive
// user profile in localStorage so the UI knows who's signed in without a round-trip on boot.
const USER_KEY = 'auth_user';
// Legacy keys from the pre-cookie scheme — proactively purged on init so stale JWTs don't linger.
const LEGACY_ACCESS_TOKEN_KEY = 'access_token';
const LEGACY_REFRESH_TOKEN_KEY = 'refresh_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private _user = signal<AuthUser | null>(this.loadUser());
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  /** Highest-privilege permission level derived from roles */
  readonly userRole = computed((): UserRole | null => {
    const roles = (this._user()?.roles ?? []).map(r => r.toUpperCase());
    if (roles.includes('SUPERADMIN')) return 'admin';
    if (roles.includes('MANAGER')) return 'manager';
    if (roles.length)              return 'employee';
    return null;
  });

  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly permissions = inject(PermissionService);

  constructor() {
    this.clearLegacyTokenStorage();
  }

  // One-time migration cleanup: tokens used to be stored in localStorage; they're now httpOnly
  // cookies. Remove any leftover token keys so the old (now-meaningless) JWTs don't hang around.
  private clearLegacyTokenStorage(): void {
    localStorage.removeItem(LEGACY_ACCESS_TOKEN_KEY);
    localStorage.removeItem(LEGACY_REFRESH_TOKEN_KEY);
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    // Server sets the httpOnly auth cookies on this response; we only persist the user profile.
    // If 2FA is required there's no session yet (no user) — the caller must call verifyTwoFactor().
    return this.http.post<AuthResponse>('/api/auth/login', request).pipe(
      tap(res => { if (!res.requiresTwoFactor && res.user) this.storeSession(res); })
    );
  }

  /** Step 2 of a 2FA login: exchange the challenge token + a code for a real session. */
  verifyTwoFactor(twoFactorToken: string, code: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/2fa/verify', { twoFactorToken, code }).pipe(
      tap(res => { if (res.user) this.storeSession(res); })
    );
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>('/api/auth/change-password', request);
  }

  updateProfile(phoneNumber: string | null): Observable<void> {
    return this.http.patch<void>('/api/auth/profile', { phoneNumber });
  }

  forgotPassword(staffId: string): Observable<ForgotPasswordResponse> {
    return this.http.post<ForgotPasswordResponse>('/api/auth/forgot-password', { staffId });
  }

  refreshToken(): Observable<AuthResponse> {
    // The refresh token rides along in its httpOnly cookie — no body needed. The server rotates it
    // and re-sets both cookies on the response.
    return this.http.post<AuthResponse>('/api/auth/refresh', {}).pipe(
      tap(res => this.storeSession(res)),
      catchError(err => {
        this.logout();
        return throwError(() => err);
      })
    );
  }

  logout(): void {
    // Best-effort server-side logout: expires the auth cookies and clears the DB refresh token.
    // We clear local state and navigate immediately regardless of the network result; the in-flight
    // request still completes and its Set-Cookie clears the cookies.
    this.http.post<void>('/api/auth/logout', {}).subscribe({ error: () => { /* ignore */ } });
    localStorage.removeItem(USER_KEY);
    this._user.set(null);
    this.permissions.clear();   // don't let the next user inherit cached permissions
    this.router.navigate(['/auth/login']);
  }

  hasRole(...checkRoles: UserRole[]): boolean {
    const permissionLevel = this.userRole();
    return permissionLevel ? checkRoles.includes(permissionLevel) : false;
  }

  private storeSession(res: AuthResponse): void {
    if (!res.user) return;
    localStorage.setItem(USER_KEY, JSON.stringify(res.user));
    this._user.set(res.user);
  }

  private loadUser(): AuthUser | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
