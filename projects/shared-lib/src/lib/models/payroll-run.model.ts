/** The steps a month's payroll run goes through. */
export type PayrollRunState = 'draft' | 'submitted' | 'approved' | 'locked';

export interface PayrollWarning {
  code: string;
  message: string;
  blocking: boolean;
}

/** One line of a payslip (earning, deduction, contribution). The amount is never negative; the section says which way it counts. */
export interface PayrollItemLine {
  type: string;
  section: string;
  description: string;
  amount: number;
}

export interface PayrollUnpaidDay {
  date: string;
  days: number;
  amount: number;
}

/** One person's line of a run. Dates are 'yyyy-MM-dd'. Money is only displayed here, never calculated. */
export interface PayrollRunLine {
  employeeId: string;
  staffId: string;
  name: string;
  excluded: boolean;
  excludedReason: string | null;
  pcb: number | null;
  calculated: boolean;
  gross: number;
  net: number;
  employerCost: number;
  unappliedDeductions: number;
  warnings: PayrollWarning[];
  items: PayrollItemLine[];
  unpaidDays: PayrollUnpaidDay[];
  signedOffBy: string | null;
  signedOffByName: string | null;
  signedOffAt: string | null;
}

export interface PayrollRunStep {
  action: string;
  staffId: string;
  name: string;
  at: string;
  reason: string | null;
}

export interface PayrollRunSummary {
  id: string;
  year: number;
  month: number;
  state: PayrollRunState;
  /** Counts every change; sent back with each action so a stale page is refused. */
  version: number;
  people: number;
  leftOut: number;
  signedOff: number;
  totalGross: number;
  totalNet: number;
  totalEmployerCost: number;
  createdAt: string;
  exportedAt: string | null;
}

export interface NotInRun {
  employeeId: string;
  staffId: string;
  name: string;
  reason: string;
}

export interface PayrollRunDetail {
  summary: PayrollRunSummary;
  calculatorVersion: string;
  divisor: number;
  hoursPerWorkingDay: number;
  deductionCapRate: number;
  lines: PayrollRunLine[];
  steps: PayrollRunStep[];
  /** Rate rows the figures rest on that the accountant has not verified; approval is refused while any exist. */
  unverifiedRates: string[];
  notInRun: NotInRun[];
}

/** A manager's view of one run: only the lines of the people who report to them. */
export interface PayrollTeam {
  runId: string;
  year: number;
  month: number;
  state: PayrollRunState;
  version: number;
  lines: PayrollRunLine[];
}

/** A run that has people who report to the signed-in manager (no money). */
export interface PayrollTeamRun {
  runId: string;
  year: number;
  month: number;
  state: PayrollRunState;
  lines: number;
  toSign: number;
}

export interface UpdateRunLineRequest {
  pcb: number | null;
  excluded: boolean;
  excludedReason: string | null;
  expectedVersion: number;
}

/** A recurring allowance or deduction on one person's pay. */
export interface PayItem {
  id: string;
  kind: 'allowance' | 'deduction';
  reason: string;
  amount: number;
  countsForEpf: boolean;
  countsForSocsoEis: boolean;
  priority: number;
  from: string;
  to: string | null;
  createdAt: string;
  /** A submitted or locked run used it: it can be ended but no longer deleted. */
  inUse: boolean;
}

export interface AddPayItemRequest {
  kind: 'allowance' | 'deduction';
  reason: string;
  amount: number;
  countsForEpf: boolean;
  countsForSocsoEis: boolean;
  priority: number;
  from: string;
  to: string | null;
}
