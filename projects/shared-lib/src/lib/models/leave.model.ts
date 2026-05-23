export type LeaveType   = 'sick' | 'annual' | 'unpaid' | 'no-show' | 'emergency';
export type LeaveStatus = 'pending' | 'approved' | 'rejected' | 'cancelled';

export interface LeaveRecord {
  id: string;
  employeeId: string;
  staffId: string;
  staffName: string;
  leaveType: LeaveType;
  startDate: string;
  endDate: string;
  startTime: string | null;  // HH:mm
  endTime: string | null;    // HH:mm
  hours: number;             // total leave duration in hours
  notes: string | null;
  rejectionReason: string | null;  // set by manager when rejecting
  status: LeaveStatus;
  createdAt: string;
}

export interface UpsertLeaveRequest {
  employeeId: string;
  leaveType: LeaveType;
  startDate: string;
  endDate: string;
  startTime: string | null;  // HH:mm
  endTime: string | null;    // HH:mm
  hours: number | null;      // explicit hours (used when times are not specified)
  notes: string | null;
  status: LeaveStatus;
  rejectionReason?: string | null;
}

export interface LeaveBalance {
  year: number;
  annualEntitlement: number;
  annualUsed: number;        // annual + emergency combined
  annualRemaining: number;
  emergencyUsed: number;     // emergency portion within annualUsed
  carryOverHours: number;    // hours carried over from previous year
  sickEntitlement: number;
  sickUsed: number;
  sickRemaining: number;
  unpaidUsed: number;
}

export interface StaffLeaveBalance {
  employeeId: string;
  staffId: string;
  fullName: string;
  year: number;
  annualEntitlement: number;
  annualUsed: number;        // annual + emergency combined
  annualRemaining: number;
  emergencyUsed: number;     // emergency portion within annualUsed
  carryOverHours: number;    // hours carried over from previous year
  sickEntitlement: number;
  sickUsed: number;
  sickRemaining: number;
  unpaidUsed: number;
}

export interface LeaveSummary {
  employeeId: string;
  staffId: string;
  fullName: string;
  year: number;
  annualUsed: number;
  sickUsed: number;
  emergencyUsed: number;
  unpaidUsed: number;
  noShowUsed: number;
  totalUsed: number;
}

export interface LeavePatternWarning {
  employeeId: string;
  staffId: string;
  fullName: string;
  sickLeaveCount: number;
  sickLeaveHours: number;
}
