import { PayrollRunState } from '../models/payroll-run.model';

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/** "October 2026". Fixed English names, so the label never depends on the browser's locale. */
export function monthLabel(year: number, month: number): string {
  return `${MONTHS[month - 1] ?? month} ${year}`;
}

/** "RM 3,000.00". Display only: the page never does arithmetic on money. */
export function rm(n: number): string {
  return `RM ${n.toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export interface RunStateTag {
  label: string;
  severity: 'secondary' | 'info' | 'warn' | 'success';
}

/** The tag a run's state is shown with. */
export function runStateTag(state: PayrollRunState): RunStateTag {
  switch (state) {
    case 'draft': return { label: 'Draft', severity: 'secondary' };
    case 'submitted': return { label: 'Waiting for sign-off', severity: 'info' };
    case 'approved': return { label: 'Approved', severity: 'warn' };
    case 'locked': return { label: 'Locked', severity: 'success' };
  }
}

/** The heading of a group of payslip lines (the calculator's section). */
export function sectionLabel(section: string): string {
  switch (section) {
    case 'Earning': return 'Earnings';
    case 'GrossDeduction': return 'Taken off gross pay';
    case 'EmployeeStatutory': return 'Statutory (employee)';
    case 'Tax': return 'Tax';
    case 'OtherDeduction': return 'Other deductions';
    case 'EmployerContribution': return 'Employer contributions';
    default: return section;
  }
}

/** One line of a run's history, in plain words. */
export function describeStep(action: string): string {
  switch (action) {
    case 'started': return 'Started the run';
    case 'edited': return 'Edited the run';
    case 'submitted': return 'Submitted it for sign-off';
    case 'returned': return 'Returned it for changes';
    case 'approved': return 'Approved it';
    case 'locked': return 'Locked it';
    case 'reopened': return 'Reopened it';
    default: return action;
  }
}
