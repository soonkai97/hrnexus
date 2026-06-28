/**
 * Formats a Date as YYYY-MM-DD using local wall-clock time.
 * Avoids the UTC-midnight → previous-day shift that `toISOString().slice(0,10)` causes
 * when the local timezone is ahead of UTC (e.g. UTC+8).
 */
export function toLocalDateString(d: Date): string {
  const y   = d.getFullYear();
  const m   = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/** Returns today's date as "YYYY-MM-DD" in local wall-clock time. */
export function todayLocalString(): string {
  return toLocalDateString(new Date());
}

/**
 * Rounds a Date to the nearest 30-minute boundary (:00 or :30), keeping the same day.
 * 15:53 → 16:00, 15:25 → 15:30, 15:14 → 15:00. Pairs with the 30-min time-picker stepper
 * so "now" defaults land on clean half-hour values. Seconds/ms are zeroed.
 */
export function roundToNearestHalfHour(d: Date): Date {
  const r = new Date(d);
  const rounded = Math.round((r.getHours() * 60 + r.getMinutes()) / 30) * 30;
  r.setHours(Math.floor(rounded / 60) % 24, rounded % 60, 0, 0);
  return r;
}

/** Current time rounded to the nearest 30-minute boundary. */
export function nowRoundedToHalfHour(): Date {
  return roundToNearestHalfHour(new Date());
}
