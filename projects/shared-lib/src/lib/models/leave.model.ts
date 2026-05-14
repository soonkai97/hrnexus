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
  days: number;
  notes: string | null;
  status: LeaveStatus;
  createdAt: string;
}

export interface UpsertLeaveRequest {
  employeeId: string;
  leaveType: LeaveType;
  startDate: string;
  endDate: string;
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
