/** One salary history row (append-only on the server). Dates are 'yyyy-MM-dd'. */
export interface SalaryProfile {
  id: string;
  effectiveFrom: string;
  basicSalary: number;
  payFrequency: 'monthly';
  bankName: string | null;
  bankAccount: string | null;
  bankHolder: string | null;
  createdAt: string;
}

export type CitizenshipStatus = 'citizen' | 'permanent-resident';

/** Employment details needed for payroll (statutory contribution rules). */
export interface EmploymentDetails {
  citizenshipStatus: CitizenshipStatus | null;
  startDate: string | null;
  endDate: string | null;
}

/** GET /api/staff/{id}/salary — current row, history (newest effective date first) and employment. */
export interface SalaryOverview {
  current: SalaryProfile | null;
  history: SalaryProfile[];
  employment: EmploymentDetails;
}

export interface AddSalaryRequest {
  basicSalary: number;
  payFrequency: 'monthly';
  effectiveFrom: string;
  bankName?: string | null;
  bankAccount?: string | null;
  bankHolder?: string | null;
}

export interface SetEmploymentRequest {
  citizenshipStatus: CitizenshipStatus;
  startDate: string;
  endDate: string | null;
}
