import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap, catchError, throwError } from 'rxjs';
import { AuthResponse, AuthUser, ChangePasswordRequest, ForgotPasswordResponse, LoginRequest, RefreshTokenRequest, UserRole } from '../models';

const ACCESS_TOKEN_KEY = 'access_token';
const REFRESH_TOKEN_KEY = 'refresh_token';
const USER_KEY = 'auth_user';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private _user = signal<AuthUser | null>(this.loadUser());
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  /** Highest-privilege permission level derived from roles */
  readonly userRole = computed((): UserRole | null => {
    const roles = this._user()?.roles ?? [];
    if (roles.some(r => r.toUpperCase() === 'ADMIN'))   return 'admin';
    if (roles.some(r => r.toUpperCase() === 'MANAGER')) return 'manager';
    if (roles.length)              return 'employee';
    return null;
  });

  constructor(private http: HttpClient, private router: Router) {
    this.evictStaleTokens();
  }

  // Clears any leftover mock tokens that can't pass real JWT validation
  private evictStaleTokens(): void {
    const token = localStorage.getItem(ACCESS_TOKEN_KEY);
    if (token && token.startsWith('mock-')) {
      localStorage.removeItem(ACCESS_TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      localStorage.removeItem(USER_KEY);
      this._user.set(null);
    }
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', request).pipe(
      tap(res => {
        this.storeSession(res);
        this.lastMustChangePassword = res.mustChangePassword ?? false;
      })
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

  forceChangePassword(newPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/force-change-password', { newPassword });
  }

  refreshToken(): Observable<AuthResponse> {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    if (!refreshToken) return throwError(() => new Error('No refresh token'));
    const body: RefreshTokenRequest = { refreshToken };
    return this.http.post<AuthResponse>('/api/auth/refresh', body).pipe(
      tap(res => this.storeSession(res)),
      catchError(err => {
        this.logout();
        return throwError(() => err);
      })
    );
  }

  logout(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this._user.set(null);
    this.router.navigate(['/auth/login']);
  }

  getAccessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  }

  /** Returns false if the stored access token is missing or its `exp` claim has passed. */
  hasValidToken(): boolean {
    const token = localStorage.getItem(ACCESS_TOKEN_KEY);
    if (!token) return false;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return (payload.exp ?? 0) > Date.now() / 1000;
    } catch {
      return false;
    }
  }

  hasRole(...checkRoles: UserRole[]): boolean {
    const permissionLevel = this.userRole();
    return permissionLevel ? checkRoles.includes(permissionLevel) : false;
  }

  private storeSession(res: AuthResponse): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, res.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, res.refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(res.user));
    this._user.set(res.user);
  }

  /** Expose the raw AuthResponse mustChangePassword from last login for navigation. */
  lastMustChangePassword = false;

  private loadUser(): AuthUser | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
