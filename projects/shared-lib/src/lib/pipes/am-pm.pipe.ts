import { Pipe, PipeTransform } from '@angular/core';

/**
 * Converts an HH:mm (24-hour) time string to 12-hour AM/PM format.
 * e.g. "20:30" → "8:30 PM"   "08:00" → "8:00 AM"   "00:15" → "12:15 AM"
 * Null/empty input returns "—".
 */
@Pipe({ name: 'amPm', standalone: true, pure: true })
export class AmPmPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    const [hStr, mStr] = value.split(':');
    const h = parseInt(hStr, 10);
    const m = parseInt(mStr, 10);
    if (isNaN(h) || isNaN(m)) return value;
    const period = h < 12 ? 'AM' : 'PM';
    const h12    = h % 12 === 0 ? 12 : h % 12;
    return `${h12}:${m.toString().padStart(2, '0')} ${period}`;
  }
}
