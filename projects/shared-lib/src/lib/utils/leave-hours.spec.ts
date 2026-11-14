import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { DEFAULT_SCHEDULE, WorkSchedule, leaveHours, leaveHoursInYear } from './leave-hours';

/**
 * The same vectors drive the backend LeaveHoursCalculatorTests, so the live form preview
 * and the server's authoritative hours can never drift apart.
 */
interface CalcVector {
  name: string;
  start: string;
  end: string;
  startTime?: string | null;
  endTime?: string | null;
  hours?: number;
  error?: boolean;
}
interface ScheduledVector extends CalcVector {
  schedule: WorkSchedule;
}
interface ScheduledYearVector extends YearVector {
  schedule: WorkSchedule;
}
interface YearVector {
  name: string;
  start: string;
  end: string;
  year: number;
  startTime?: string | null;
  endTime?: string | null;
  hours: number;
}

const fixtures = JSON.parse(readFileSync(resolve('tests/fixtures/leave-hours.json'), 'utf8')) as {
  calculate: CalcVector[];
  hoursInYear: YearVector[];
  scheduled: ScheduledVector[];
  scheduledInYear: ScheduledYearVector[];
};

describe('leave-hours', () => {
  describe('leaveHours (shared vectors)', () => {
    it('loads the shared vectors', () => {
      expect(fixtures.calculate.length).toBeGreaterThan(0);
    });

    for (const v of fixtures.calculate) {
      it(v.name, () => {
        const result = leaveHours(v.start, v.end, v.startTime ?? null, v.endTime ?? null);
        if (v.error) expect(result).toBeNull();
        else expect(result).toBe(v.hours);
      });
    }
  });

  describe('leaveHoursInYear (shared vectors)', () => {
    it('loads the shared vectors', () => {
      expect(fixtures.hoursInYear.length).toBeGreaterThan(0);
    });

    for (const v of fixtures.hoursInYear) {
      it(v.name, () => {
        expect(
          leaveHoursInYear(v.start, v.end, v.startTime ?? null, v.endTime ?? null, v.year),
        ).toBe(v.hours);
      });
    }
  });

  describe('company schedule (shared vectors)', () => {
    it('the default schedule is Monday to Friday, 8 hours', () => {
      expect(DEFAULT_SCHEDULE).toEqual({ days: [1, 2, 3, 4, 5], hours: 8 });
    });

    it('loads the shared vectors', () => {
      expect(fixtures.scheduled.length).toBeGreaterThan(0);
      expect(fixtures.scheduledInYear.length).toBeGreaterThan(0);
    });

    for (const v of fixtures.scheduled) {
      it(v.name, () => {
        const result = leaveHours(
          v.start,
          v.end,
          v.startTime ?? null,
          v.endTime ?? null,
          v.schedule,
        );
        if (v.error) expect(result).toBeNull();
        else expect(result).toBe(v.hours);
      });
    }

    for (const v of fixtures.scheduledInYear) {
      it(v.name, () => {
        expect(
          leaveHoursInYear(
            v.start,
            v.end,
            v.startTime ?? null,
            v.endTime ?? null,
            v.year,
            v.schedule,
          ),
        ).toBe(v.hours);
      });
    }

    it('without a schedule argument the Monday to Friday default applies', () => {
      // A Saturday is refused by default; the same day is a full day for a Mon-Sat company.
      expect(leaveHours('2026-06-06', '2026-06-06')).toBeNull();
      expect(
        leaveHours('2026-06-06', '2026-06-06', null, null, { days: [1, 2, 3, 4, 5, 6], hours: 8 }),
      ).toBe(8);
    });
  });

  describe('Date inputs', () => {
    it('accepts Date objects built from local calendar parts (no timezone shift)', () => {
      // Mon 1 Jun to Wed 3 Jun 2026 is three working days = 24h, not a clock difference.
      expect(leaveHours(new Date(2026, 5, 1), new Date(2026, 5, 3), null, null)).toBe(24);
    });
  });
});
