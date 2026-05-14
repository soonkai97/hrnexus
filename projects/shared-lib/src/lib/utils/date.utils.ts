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
