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

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  user: AuthUser;
  mustChangePassword: boolean;
}

export interface AuthUser {
  id: string;
  staffId: string;
  fullName: string;
  email: string;
  /** All job roles assigned to this employee (e.g. ["bartender", "manager"]) */
  roles: string[];
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface ForgotPasswordRequest  { staffId: string; }
export interface ForgotPasswordResponse { message: string; found: boolean; }
export interface ForceChangePasswordRequest { newPassword: string; }
