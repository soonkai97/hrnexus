/**
 * Password policy shared across every password-entry surface (change-password,
 * staff register, admin reset). Kept in sync with the backend `PasswordPolicy`:
 * at least 8 characters, one uppercase letter, one number.
 */
export interface PasswordChecks {
  minLength: boolean;
  hasUpper: boolean;
  hasNumber: boolean;
}

export function passwordChecks(password: string | null | undefined): PasswordChecks {
  const v = password ?? '';
  return {
    minLength: v.length >= 8,
    hasUpper: /[A-Z]/.test(v),
    hasNumber: /[0-9]/.test(v),
  };
}

export function isStrongPassword(password: string | null | undefined): boolean {
  const c = passwordChecks(password);
  return c.minLength && c.hasUpper && c.hasNumber;
}
