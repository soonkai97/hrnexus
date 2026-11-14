/** The key of a leave type in the company's catalogue (annual, sick, maternity, or one the company added). */
export type LeaveType   = string;
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
  types?: LeaveTypeBalance[]; // one line per leave type of the catalogue (Phase 7q)
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
  types?: LeaveTypeBalance[]; // one line per leave type of the catalogue (Phase 7q)
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
  types?: LeaveTypeBalance[]; // one line per leave type of the catalogue (Phase 7q)
}

export interface LeavePatternWarning {
  employeeId: string;
  staffId: string;
  fullName: string;
  sickLeaveCount: number;
  sickLeaveHours: number;
}

/** Where a leave type's hours are charged. */
export type LeavePool = 'annual' | 'sick' | 'own' | 'none';

/** The tag colours a leave type can use (they are the UI's tag severities). */
export type LeaveColour = 'info' | 'success' | 'warn' | 'danger' | 'secondary' | 'contrast';

/** One leave type of the company's catalogue (mirror of the API's LeaveTypeDto). */
export interface LeaveTypeDefinition {
  /** Immutable slug stored on every leave request. */
  key: string;
  label: string;
  colour: LeaveColour;
  sortOrder: number;
  active: boolean;
  /** Sick and no-show: behaviour is fixed and they cannot be deactivated. */
  isSystem: boolean;
  pool: LeavePool;
  /** Yearly allowance in days for an own pool; null otherwise. */
  allowanceDays: number | null;
  /** Longest single request in calendar days (replaces the policy cap); null keeps the policy cap. */
  maxDaysPerRequest: number | null;
  autoApprove: boolean;
  advanceNoticeDays: number | null;
  /** The notice is the company policy's annual-leave notice. */
  advanceNoticeFromPolicy: boolean;
  deductsPay: boolean;
  adminOnly: boolean;
  /** Counts consecutive calendar days (weekends included) instead of working days. */
  countsCalendarDays: boolean;
  /** Messages never name the type. */
  sensitive: boolean;
  /** Only filled for the settings page: the type already has leave, so its pool and day counting are locked. */
  hasLeave: boolean;
}

export interface LeaveTypeCatalogue {
  /** Counts saves; 0 while the code defaults apply. A save from a stale copy is refused (409). */
  version: number;
  isSaved: boolean;
  types: LeaveTypeDefinition[];
}

/** One type's line on a balance or summary (mirror of the API's LeaveTypeBalanceDto). */
export interface LeaveTypeBalance {
  key: string;
  label: string;
  colour: LeaveColour;
  pool: LeavePool;
  active: boolean;
  used: number;
  /** The balance the type is charged to (hours); null for a type with no balance. */
  entitlement: number | null;
  /** Hours used from that balance by every type sharing it. */
  poolUsed: number | null;
  remaining: number | null;
}
