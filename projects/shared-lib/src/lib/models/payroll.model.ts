export interface PaySummary {
  employeeId: string;
  periodStart: string;
  periodEnd: string;
  totalHours: number;
  regularHours: number;
  overtimeHours: number;
  regularPay: number;
  overtimePay: number;
  tipsAllocated: number;
  grossPay: number;
  shifts: PayShiftLine[];
}

export interface PayShiftLine {
  shiftId: string;
  date: string;
  locationName: string;
  hours: number;
  pay: number;
}
