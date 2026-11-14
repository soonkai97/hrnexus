/**
 * Leave hours are counted in WORKING DAYS of the company's schedule, not clock time.
 * One rule shared with the backend (LeaveHoursCalculator); both are tested against
 * tests/fixtures/leave-hours.json. The server is authoritative; this powers the live form preview.
 *
 * The schedule is the company policy (working weekdays and hours per day); the default is Mon-Fri, 8h.
 * - Single working day: no times = a full day; both times = (end - start) hours, must be > 0, capped at a full day.
 * - Multi-day: working days in the inclusive range x hours per day; times are ignored.
 * - Invalid (end before start, exactly one time, no working day in the range, non-positive span) = null.
 */
export interface WorkSchedule {
  /** ISO weekday numbers: 1 = Monday ... 7 = Sunday. */
  days: readonly number[];
  /** Hours in one working day. */
  hours: number;
}

/** The schedule in force until the company saves its own: Monday to Friday, 8 hours. */
export const DEFAULT_SCHEDULE: WorkSchedule = { days: [1, 2, 3, 4, 5], hours: 8 };

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

/** ISO weekday of a UTC day: 1 = Monday ... 7 = Sunday. */
function isoDay(utcDay: number): number {
  return new Date(utcDay).getUTCDay() || 7;
}

/** Working days of the schedule in [startDay, endDay], optionally restricted to one calendar year. */
function workingDays(
  startDay: number,
  endDay: number,
  schedule: WorkSchedule,
  year?: number,
): number {
  let count = 0;
  for (let d = startDay; d <= endDay; d += DAY_MS) {
    if (!schedule.days.includes(isoDay(d))) continue;
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
  schedule: WorkSchedule = DEFAULT_SCHEDULE,
): number | null {
  if (start == null || end == null) return null;
  const s = toUtcDay(start);
  const e = toUtcDay(end);
  if (s === null || e === null || e < s) return null;

  const hasStart = !!startTime;
  const hasEnd = !!endTime;
  if (hasStart !== hasEnd) return null;

  if (workingDays(s, e, schedule) === 0) return null;

  if (s === e) {
    if (!hasStart) return schedule.hours;
    const st = toMinutes(startTime!);
    const et = toMinutes(endTime!);
    if (st === null || et === null || et <= st) return null;
    return Math.min((et - st) / 60, schedule.hours);
  }

  return workingDays(s, e, schedule) * schedule.hours;
}

/**
 * The part of a leave request that falls in a calendar year (for per-year balances).
 * Single day: all its hours if it is in that year, else 0. Multi-day: that year's working days x hours per day.
 * Returns null when the request itself is invalid.
 */
export function leaveHoursInYear(
  start: string | Date | null | undefined,
  end: string | Date | null | undefined,
  startTime: string | null | undefined,
  endTime: string | null | undefined,
  year: number,
  schedule: WorkSchedule = DEFAULT_SCHEDULE,
): number | null {
  const total = leaveHours(start, end, startTime, endTime, schedule);
  if (total === null) return null;
  const s = toUtcDay(start!)!;
  const e = toUtcDay(end!)!;
  if (s === e) return new Date(s).getUTCFullYear() === year ? total : 0;
  return workingDays(s, e, schedule, year) * schedule.hours;
}
