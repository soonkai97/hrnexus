import { Pipe, PipeTransform } from '@angular/core';

/**
 * Formats an HH:mm time string for display in 24-hour form, zero-padded.
 * e.g. "8:30" → "08:30"   "20:5" → "20:05"   "00:00" → "00:00" (midnight)   "12:00" → "12:00" (noon).
 * Null/empty → "—"; non-time strings pass through unchanged.
 *
 * (Replaces the former AmPmPipe — the venue displays 24-hour time per customer request.)
 */
@Pipe({ name: 'hm', standalone: true, pure: true })
export class HmPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    const [hStr, mStr] = value.split(':');
    const h = parseInt(hStr ?? '', 10);
    const m = parseInt(mStr ?? '', 10);
    if (isNaN(h) || isNaN(m)) return value;
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
  }
}
