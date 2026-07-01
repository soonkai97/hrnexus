import { passwordChecks, isStrongPassword, PASSWORD_MIN_LENGTH } from './password.utils';

/**
 * Password policy shared by every password-entry surface and mirrored on the backend
 * (PasswordPolicy.cs). WHY it matters: a drift between FE checklist and BE enforcement
 * would let users "pass" the checklist only to be rejected by the API (or vice versa).
 * The policy is a length floor only — no character-class rules (venue preference).
 */
describe('password.utils', () => {
  it('exposes the minimum length constant (6)', () => {
    expect(PASSWORD_MIN_LENGTH).toBe(6);
  });

  describe('passwordChecks', () => {
    it('reports whether the minimum length is met', () => {
      expect(passwordChecks('abc')).toEqual({ minLength: false });     // 3 < 6
      expect(passwordChecks('abcde')).toEqual({ minLength: false });   // 5 < 6
      expect(passwordChecks('abcdef')).toEqual({ minLength: true });   // 6
      expect(passwordChecks('lowercaseonly')).toEqual({ minLength: true });
    });

    it('treats null/undefined as an empty password (not met)', () => {
      expect(passwordChecks(null)).toEqual({ minLength: false });
      expect(passwordChecks(undefined)).toEqual({ minLength: false });
    });
  });

  describe('isStrongPassword', () => {
    it('is a length floor only — accepts any 6+ char password regardless of characters', () => {
      expect(isStrongPassword('abcdef')).toBe(true);      // lowercase, no number — OK now
      expect(isStrongPassword('password')).toBe(true);
      expect(isStrongPassword('123456')).toBe(true);
      expect(isStrongPassword('Passw0rd')).toBe(true);
    });

    it('rejects passwords below the minimum length', () => {
      expect(isStrongPassword('Pass1')).toBe(false);      // 5 chars
      expect(isStrongPassword('abc')).toBe(false);
      expect(isStrongPassword('')).toBe(false);
    });
  });
});
