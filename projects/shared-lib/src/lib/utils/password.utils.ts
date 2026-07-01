/**
 * Password policy shared across every password-entry surface (change-password,
 * staff register, admin reset). Kept in sync with the backend `PasswordPolicy`:
 * a minimum length only (no character-class rules), per the venue's preference for
 * low friction. Brute-force is guarded by account lockout + IP rate limiting.
 */
export const PASSWORD_MIN_LENGTH = 6;

export interface PasswordChecks {
  minLength: boolean;
}

export function passwordChecks(password: string | null | undefined): PasswordChecks {
  const v = password ?? '';
  return {
    minLength: v.length >= PASSWORD_MIN_LENGTH,
  };
}

export function isStrongPassword(password: string | null | undefined): boolean {
  return passwordChecks(password).minLength;
}
