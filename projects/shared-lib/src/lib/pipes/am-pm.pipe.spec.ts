import { AmPmPipe } from './am-pm.pipe';

/**
 * AmPmPipe — converts HH:mm (24-hour) to 12-hour AM/PM.
 *
 * WHY these tests matter:
 *  - Schedule displays throughout the app rely on readable times for staff.
 *  - Midnight (00:xx) and noon (12:xx) are well-known edge cases for 12-hour conversion.
 *  - Null/empty guard prevents template errors when shift times are not yet set.
 */
describe('AmPmPipe', () => {
  let pipe: AmPmPipe;

  beforeEach(() => {
    pipe = new AmPmPipe();
  });

  describe('null / empty guard', () => {
    it('should return em-dash for null so templates never render undefined', () => {
      expect(pipe.transform(null)).toBe('—');
    });

    it('should return em-dash for undefined', () => {
      expect(pipe.transform(undefined)).toBe('—');
    });

    it('should return em-dash for empty string', () => {
      expect(pipe.transform('')).toBe('—');
    });
  });

  describe('standard AM/PM conversion', () => {
    it('should convert a typical PM time — staff rely on seeing "8:30 PM" not "20:30"', () => {
      expect(pipe.transform('20:30')).toBe('8:30 PM');
    });

    it('should convert a morning AM time', () => {
      expect(pipe.transform('08:05')).toBe('8:05 AM');
    });

    it('should zero-pad minutes correctly so "09:00" becomes "9:00 AM" not "9:0 AM"', () => {
      expect(pipe.transform('09:00')).toBe('9:00 AM');
    });

    it('should preserve leading zero in minutes for "14:03"', () => {
      expect(pipe.transform('14:03')).toBe('2:03 PM');
    });
  });

  describe('midnight edge case (00:xx)', () => {
    it('should show "12:15 AM" for midnight 00:15 — not "0:15 AM"', () => {
      // 0 % 12 === 0 → 12, period = AM
      expect(pipe.transform('00:15')).toBe('12:15 AM');
    });

    it('should show "12:00 AM" for exactly midnight', () => {
      expect(pipe.transform('00:00')).toBe('12:00 AM');
    });
  });

  describe('noon edge case (12:xx)', () => {
    it('should show "12:00 PM" for noon — not "0:00 PM"', () => {
      // 12 % 12 === 0 → 12, period = PM
      expect(pipe.transform('12:00')).toBe('12:00 PM');
    });

    it('should show "12:45 PM" for 12:45', () => {
      expect(pipe.transform('12:45')).toBe('12:45 PM');
    });
  });

  describe('invalid input passthrough', () => {
    it('should return the original value for non-time strings so data is never silently lost', () => {
      expect(pipe.transform('invalid')).toBe('invalid');
    });

    it('should return the original value when minutes part is missing', () => {
      expect(pipe.transform('20')).toBe('20');
    });

    it('should return the original value for alphabetic input like "ab:cd"', () => {
      expect(pipe.transform('ab:cd')).toBe('ab:cd');
    });
  });
});
