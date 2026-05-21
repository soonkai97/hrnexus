export type LeaveType   = 'sick' | 'annual' | 'unpaid' | 'no-show';
export type LeaveStatus = 'pending' | 'approved' | 'rejected';

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
}


export interface LeaveBalance {
  year: number;
  annualEntitlement: number;
  annualUsed: number;
  annualRemaining: number;
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
  annualUsed: number;
  annualRemaining: number;
  sickEntitlement: number;
  sickUsed: number;
  sickRemaining: number;
  unpaidUsed: number;
}
