export interface LoginRequest {
  staffId: string;
  password: string;
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
  /** Assigned role NAMES, for display (the profile card). What a person may open or do is decided by permissions, never by a role's name. */
  roles: string[];
}

export interface ForgotPasswordRequest  { staffId: string; }
export interface ForgotPasswordResponse { message: string; found: boolean; }
