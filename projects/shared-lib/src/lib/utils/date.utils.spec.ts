import { toLocalDateString, todayLocalString, roundToNearestHalfHour } from './date.utils';

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

describe('roundToNearestHalfHour', () => {
  // Snaps "now" defaults to a clean :00/:30 so they pair with the 30-minute picker stepper.
  const at = (h: number, m: number) => roundToNearestHalfHour(new Date(2026, 0, 15, h, m, 30, 500));

  it('rounds 15:53 up to 16:00 (crosses the hour) — the reported expectation', () => {
    const r = at(15, 53);
    expect(r.getHours()).toBe(16);
    expect(r.getMinutes()).toBe(0);
  });

  it('rounds 15:25 up to 15:30 (nearest boundary)', () => {
    expect(at(15, 25).getMinutes()).toBe(30);
  });

  it('rounds 15:14 down to 15:00 (nearest boundary)', () => {
    const r = at(15, 14);
    expect(r.getHours()).toBe(15);
    expect(r.getMinutes()).toBe(0);
  });

  it('rounds the 15-minute midpoint up to 15:30', () => {
    expect(at(15, 15).getMinutes()).toBe(30);
  });

  it('leaves an exact boundary unchanged and zeroes seconds/ms', () => {
    const r = at(9, 30);
    expect(r.getHours()).toBe(9);
    expect(r.getMinutes()).toBe(30);
    expect(r.getSeconds()).toBe(0);
    expect(r.getMilliseconds()).toBe(0);
  });

  it('wraps 23:53 to 00:00 rather than producing an invalid 24:00', () => {
    const r = at(23, 53);
    expect(r.getHours()).toBe(0);
    expect(r.getMinutes()).toBe(0);
  });

  it('does not mutate the input date', () => {
    const d = new Date(2026, 0, 15, 15, 53, 30, 0);
    roundToNearestHalfHour(d);
    expect(d.getMinutes()).toBe(53);
  });
});
