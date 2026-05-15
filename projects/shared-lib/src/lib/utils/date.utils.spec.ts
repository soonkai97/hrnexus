import { toLocalDateString, todayLocalString, MONTH_NAMES } from './date.utils';

describe('toLocalDateString', () => {
  it('formats year, month, and day using local wall-clock time', () => {
    // new Date(y, m, d) creates local midnight — the reference for wall-clock formatting
    expect(toLocalDateString(new Date(2025, 0, 15))).toBe('2025-01-15'); // Jan 15
    expect(toLocalDateString(new Date(2024, 11, 31))).toBe('2024-12-31'); // Dec 31
  });

  it('zero-pads single-digit months and days', () => {
    expect(toLocalDateString(new Date(2025, 0, 5))).toBe('2025-01-05');  // month=01, day=05
    expect(toLocalDateString(new Date(2025, 8, 1))).toBe('2025-09-01');  // month=09, day=01
  });

  it('reads local getDate() not UTC getUTCDate(), preventing the UTC+8 off-by-one bug', () => {
    // In UTC+8, Jan 15 00:00 local = Jan 14 16:00 UTC.
    // toISOString().slice(0,10) would return "2025-01-14" — the wrong day.
    // toLocalDateString must always return the wall-clock local date.
    const localMidnight = new Date(2025, 0, 15);
    const wallClockExpected =
      `${localMidnight.getFullYear()}-` +
      `${String(localMidnight.getMonth() + 1).padStart(2, '0')}-` +
      `${String(localMidnight.getDate()).padStart(2, '0')}`;

    expect(toLocalDateString(localMidnight)).toBe(wallClockExpected);
  });
});

describe('todayLocalString', () => {
  it('returns a YYYY-MM-DD formatted string', () => {
    expect(todayLocalString()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('equals toLocalDateString of the current instant', () => {
    // Sample before and after so a midnight crossing can't cause a spurious failure
    const before = toLocalDateString(new Date());
    const result  = todayLocalString();
    const after  = toLocalDateString(new Date());
    expect([before, after]).toContain(result);
  });
});

describe('MONTH_NAMES', () => {
  it('contains exactly 12 entries', () => {
    expect(MONTH_NAMES).toHaveLength(12);
  });

  it('starts with January and ends with December', () => {
    expect(MONTH_NAMES[0]).toBe('January');
    expect(MONTH_NAMES[11]).toBe('December');
  });

  it('is zero-index-aligned with Date.getMonth()', () => {
    // Components use MONTH_NAMES[new Date().getMonth()] to render the current month label
    expect(MONTH_NAMES[new Date(2025, 3, 1).getMonth()]).toBe('April');  // April = index 3
    expect(MONTH_NAMES[new Date(2025, 6, 1).getMonth()]).toBe('July');   // July  = index 6
  });
});
