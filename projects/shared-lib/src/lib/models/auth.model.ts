export type UserRole = 'employee' | 'manager' | 'admin';

export interface LoginRequest {
  staffId: string;
  password: string;
  portal?: 'staff' | 'admin';
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
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

export interface AuthUser {
  id: string;
  staffId: string;
  fullName: string;
  email: string;
  /** All job roles assigned to this employee (e.g. ["bartender", "manager"]) */
  roles: string[];
}

export interface ForgotPasswordRequest  { staffId: string; }
export interface ForgotPasswordResponse { message: string; found: boolean; }
