import { passwordChecks, isStrongPassword } from './password.utils';

/**
 * Password policy shared by every password-entry surface and mirrored on the backend
 * (PasswordPolicy.cs). WHY it matters: a drift between FE checklist and BE enforcement
 * would let users "pass" the checklist only to be rejected by the API (or vice versa).
 */
describe('password.utils', () => {
  describe('passwordChecks', () => {
    it('reports each requirement independently', () => {
      expect(passwordChecks('abc')).toEqual({ minLength: false, hasUpper: false, hasNumber: false });
      expect(passwordChecks('abcdefgh')).toEqual({ minLength: true, hasUpper: false, hasNumber: false });
      expect(passwordChecks('Abcdefgh')).toEqual({ minLength: true, hasUpper: true, hasNumber: false });
      expect(passwordChecks('Abcdefg9')).toEqual({ minLength: true, hasUpper: true, hasNumber: true });
    });

    it('treats null/undefined as an empty password (all false)', () => {
      expect(passwordChecks(null)).toEqual({ minLength: false, hasUpper: false, hasNumber: false });
      expect(passwordChecks(undefined)).toEqual({ minLength: false, hasUpper: false, hasNumber: false });
    });
  });

  describe('isStrongPassword', () => {
    it('is true only when all three requirements are met', () => {
      expect(isStrongPassword('Passw0rd')).toBe(true);
      expect(isStrongPassword('password1')).toBe(false);   // no uppercase
      expect(isStrongPassword('PASSWORD1')).toBe(true);    // uppercase + number + length
      expect(isStrongPassword('Pass1')).toBe(false);       // too short
      expect(isStrongPassword('Password')).toBe(false);    // no number
      expect(isStrongPassword('')).toBe(false);
    });
  });
});
