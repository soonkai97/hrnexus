export type UserRole = 'employee' | 'manager' | 'admin';

export interface LoginRequest {
  staffId: string;
  password: string;
  portal?: 'staff' | 'admin';
}

/**
 * Login/refresh response. Tokens are NOT in the body — they're set by the server as httpOnly
 * cookies (XSS can't read them). The body carries only the non-sensitive user info the UI needs.
 *
 */
export interface AuthResponse {
  expiresIn?: number;
  user?: AuthUser;
}

/** An assigned job/role, carrying its GUID id + localizable display names so the staff-app
 * log-shift dropdown can render a language-aware label and submit the role's id on clock-in. */
export interface AuthJob {
  id: string;
  name: string;
  nameZh?: string | null;
  nameVi?: string | null;
  /** 'hourly' | 'fixed' — drives the log-shift check-out picker's minute step. Optional so a
   *  pre-upgrade cached session (no payType) still works (falls back to the hourly default). */
  payType?: string | null;
}

export interface AuthUser {
  id: string;
  staffId: string;
  fullName: string;
  email: string;
  /** Assigned role NAMES (e.g. ["SUPERADMIN", "MANAGER"]) — drives the FE auth-tier logic. */
  roles: string[];
  /** Assigned jobs (id + localizable names) — source for the staff-app log-shift role dropdown. */
  jobs: AuthJob[];
}

export interface ForgotPasswordRequest  { staffId: string; }
export interface ForgotPasswordResponse { message: string; found: boolean; }
