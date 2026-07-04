import { HmPipe } from './hm.pipe';

/**
 * HmPipe — formats HH:mm time strings for 24-hour display.
 *
 * WHY these tests matter:
 *  - Schedule displays throughout the app rely on a consistent 24-hour readout for staff.
 *  - Midnight (00:xx) and noon (12:xx) are the classic edge cases — they must stay 00/12, not fold.
 *  - Null/empty guard prevents template errors when shift times are not yet set.
 */
describe('HmPipe', () => {
  let pipe: HmPipe;

  beforeEach(() => { pipe = new HmPipe(); });

  describe('null / empty guard', () => {
    it('returns em-dash for null so templates never render undefined', () => {
      expect(pipe.transform(null)).toBe('—');
    });
    it('returns em-dash for undefined', () => {
      expect(pipe.transform(undefined)).toBe('—');
    });
    it('returns em-dash for empty string', () => {
      expect(pipe.transform('')).toBe('—');
    });
  });

  describe('24-hour formatting', () => {
    it('keeps an evening time in 24-hour form — staff see "20:30" not "8:30 PM"', () => {
      expect(pipe.transform('20:30')).toBe('20:30');
    });
    it('keeps a morning time in 24-hour form', () => {
      expect(pipe.transform('08:05')).toBe('08:05');
    });
    it('zero-pads a single-digit hour and minute', () => {
      expect(pipe.transform('8:5')).toBe('08:05');
    });
    it('renders midnight as 00:00 (not folded to 12)', () => {
      expect(pipe.transform('00:00')).toBe('00:00');
    });
    it('renders noon as 12:00 (not folded to 00)', () => {
      expect(pipe.transform('12:00')).toBe('12:00');
    });
  });

  describe('invalid input passthrough', () => {
    it('returns the original value for non-time strings so data is never silently lost', () => {
      expect(pipe.transform('invalid')).toBe('invalid');
    });
    it('returns the original value when the minutes part is missing', () => {
      expect(pipe.transform('20')).toBe('20');
    });
    it('returns the original value for alphabetic input like "ab:cd"', () => {
      expect(pipe.transform('ab:cd')).toBe('ab:cd');
    });
  });
});
