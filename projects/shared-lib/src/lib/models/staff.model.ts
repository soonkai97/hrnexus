export interface Staff {
  id: string;
  staffId: string;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string | null;
  roles: string[];
  locationIds: string[];
  isActive: boolean;
  phoneNumber: string | null;
  icNo: string | null;
  passportNo: string | null;
  dateOfBirth: string | null;
  age: number | null;
}

export interface RegisterStaffRequest {
  staffId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  password: string;
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

export interface StaffRole {
  name: string;
  isSystem: boolean;
  hourlyRate: number;
  /** Optional Chinese name; shown instead of `name` when the UI language is Chinese. */
  nameZh?: string | null;
}
