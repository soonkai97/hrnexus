import { describe, it, expect } from 'vitest';
import { describeStep, monthLabel, rm, runStateTag, sectionLabel } from './payroll-format';

/**
 * WHY these tests matter: these words are what a person reads before they approve someone's pay. A wrong month name or a
 * state shown with the wrong tag would send an approval to the wrong month or hide that a run is still waiting.
 */
describe('payroll-format', () => {
  it('names the month in English whatever the browser locale is', () => {
    expect(monthLabel(2026, 10)).toBe('October 2026');
    expect(monthLabel(2027, 1)).toBe('January 2027');
    expect(monthLabel(2026, 12)).toBe('December 2026');
  });

  it('formats money with two decimals and thousands separators', () => {
    expect(rm(3000)).toBe('RM 3,000.00');
    expect(rm(115.38)).toBe('RM 115.38');
    expect(rm(0)).toBe('RM 0.00');
  });

  it('gives every state its own label and tag', () => {
    expect(runStateTag('draft')).toEqual({ label: 'Draft', severity: 'secondary' });
    expect(runStateTag('submitted').label).toBe('Waiting for sign-off');
    expect(runStateTag('approved').severity).toBe('warn');
    expect(runStateTag('locked')).toEqual({ label: 'Locked', severity: 'success' });
  });

  it('titles the payslip sections and falls back to the raw name', () => {
    expect(sectionLabel('Earning')).toBe('Earnings');
    expect(sectionLabel('EmployeeStatutory')).toBe('Statutory (employee)');
    expect(sectionLabel('EmployerContribution')).toBe('Employer contributions');
    expect(sectionLabel('Mystery')).toBe('Mystery');
  });

  it('describes each step of the history in plain words', () => {
    expect(describeStep('submitted')).toBe('Submitted it for sign-off');
    expect(describeStep('returned')).toBe('Returned it for changes');
    expect(describeStep('reopened')).toBe('Reopened it');
    expect(describeStep('something-new')).toBe('something-new');
  });
});
