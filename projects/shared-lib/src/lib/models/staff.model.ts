export interface Staff {
  id: string;
  staffId: string;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string | null;
  /** Assigned role NAMES (display) — the staff-detail response returns names for the roles chips. */
  roles: string[];
  locationIds: string[];
  isActive: boolean;
  phoneNumber: string | null;
  icNo: string | null;
  passportNo: string | null;
  dateOfBirth: string | null;
  age: number | null;
  /** When true, this account is hidden from the roster by default (seeded system accounts). Optional so
   *  a cached list response from before this field shipped still parses; treated as false when absent. */
  isHidden?: boolean;
}

export interface RegisterStaffRequest {
  staffId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  password: string;
  /** Assigned role IDS (GUIDs) — roles are identified by id since the GUID-PK migration. */
  roles: string[];
  locations: string[];
  phoneNumber: string;
  icNo: string | null;
  passportNo: string | null;
  dateOfBirth: string | null;
}

export interface UpdateStaffRequest {
  staffId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  /** Assigned role IDS (GUIDs) — roles are identified by id since the GUID-PK migration. */
  roles: string[];
  locations: string[];
  phoneNumber: string | null;
  icNo: string | null;
  passportNo: string | null;
  dateOfBirth: string | null;
}

export interface StaffDetail extends Staff {
  createdAt: string;
}

/** How a job is paid. 'hourly' = hours × rate; 'fixed' = a flat amount per shift. */
export type PayType = 'hourly' | 'fixed';

export interface StaffRole {
  /** GUID primary key — used as the value for role pickers and for clock-in / rate / delete calls. */
  id: string;
  name: string;
  isSystem: boolean;
  hourlyRate: number;
  /** 'hourly' = the rate is per hour; 'fixed' = the rate is a flat fee for the whole shift. */
  payType: PayType;
  /** Optional Chinese name; shown instead of `name` when the UI language is Chinese. */
  nameZh?: string | null;
  /** Optional Vietnamese name; shown instead of `name` when the UI language is Vietnamese. */
  nameVi?: string | null;
  /** Maximum number of days a single shift for this job may span (1–30). Enforced on clock-out and
   *  manual/admin entry. Optional so a cached response from just before this field shipped still
   *  parses; read sites default a missing value to 30. */
  maxShiftDays?: number;
}
