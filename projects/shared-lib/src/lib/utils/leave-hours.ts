/**
 * Leave hours are counted in WORKING DAYS (Mon-Fri, 8h each), not clock time.
 * One rule shared with the backend (LeaveHoursCalculator); both are tested against
 * tests/fixtures/leave-hours.json. The server is authoritative; this powers the live form preview.
 *
 * - Single weekday: no times = 8h; both times = (end - start) hours, must be > 0, capped at 8h.
 * - Multi-day: working days in the inclusive range x 8h; times are ignored.
 * - Invalid (end before start, exactly one time, no Mon-Fri day, non-positive span) = null.
 */
export const WORKDAY_HOURS = 8;

const DAY_MS = 86_400_000;

/** Calendar date as UTC midnight so results never depend on the machine timezone. */
function toUtcDay(d: string | Date): number | null {
  if (d instanceof Date) {
    if (isNaN(d.getTime())) return null;
    // A Date from a picker is local midnight: take its local calendar parts.
    return Date.UTC(d.getFullYear(), d.getMonth(), d.getDate());
  }
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(d);
  if (!m) return null;
  const [, y = '', mo = '', day = ''] = m;
  return Date.UTC(+y, +mo - 1, +day);
}

function toMinutes(t: string): number | null {
  const m = /^(\d{1,2}):(\d{2})/.exec(t);
  if (!m) return null;
  const [, hh = '', mm = ''] = m;
  const h = +hh;
  const min = +mm;
  return h > 23 || min > 59 ? null : h * 60 + min;
}

function isWeekday(utcDay: number): boolean {
  const dow = new Date(utcDay).getUTCDay();
  return dow !== 0 && dow !== 6;
}

/** Mon-Fri days in [startDay, endDay], optionally restricted to one calendar year. */
function workingDays(startDay: number, endDay: number, year?: number): number {
  let count = 0;
  for (let d = startDay; d <= endDay; d += DAY_MS) {
    if (!isWeekday(d)) continue;
    if (year !== undefined && new Date(d).getUTCFullYear() !== year) continue;
    count++;
  }
  return count;
}

/** Hours for a leave request, or null when the input is invalid. Never throws. */
export function leaveHours(
  start: string | Date | null | undefined,
  end: string | Date | null | undefined,
  startTime?: string | null,
  endTime?: string | null,
): number | null {
  if (start == null || end == null) return null;
  const s = toUtcDay(start);
  const e = toUtcDay(end);
  if (s === null || e === null || e < s) return null;

  const hasStart = !!startTime;
  const hasEnd = !!endTime;
  if (hasStart !== hasEnd) return null;

  if (workingDays(s, e) === 0) return null;

  if (s === e) {
    if (!hasStart) return WORKDAY_HOURS;
    const st = toMinutes(startTime!);
    const et = toMinutes(endTime!);
    if (st === null || et === null || et <= st) return null;
    return Math.min((et - st) / 60, WORKDAY_HOURS);
  }

  return workingDays(s, e) * WORKDAY_HOURS;
}

/**
 * The part of a leave request that falls in a calendar year (for per-year balances).
 * Single day: all its hours if it is in that year, else 0. Multi-day: that year's working days x 8h.
 * Returns null when the request itself is invalid.
 */
export function leaveHoursInYear(
  start: string | Date | null | undefined,
  end: string | Date | null | undefined,
  startTime: string | null | undefined,
  endTime: string | null | undefined,
  year: number,
): number | null {
  const total = leaveHours(start, end, startTime, endTime);
  if (total === null) return null;
  const s = toUtcDay(start!)!;
  const e = toUtcDay(end!)!;
  if (s === e) return new Date(s).getUTCFullYear() === year ? total : 0;
  return workingDays(s, e, year) * WORKDAY_HOURS;
}
