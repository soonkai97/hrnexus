import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface DashboardStats {
  onShiftCount:    number;
  totalStaff:      number;
  tonightHours:    number;
  tonightCost:     number;
  openShiftsCount: number;
}

export interface ActiveStaffDto {
  workLogId:      string;
  staffId:        string;
  name:           string;
  role:           string;
  locationName:   string;
  workDate:       string;   // yyyy-MM-dd — the shift's clock-in date
  startTime:      string;   // HH:mm — the shift's clock-in time
  clockedInAt:    string;
  minutesOnShift: number;
  notes?:         string | null;
  /** GPS captured from the staff device at clock-in (null if denied/unavailable). */
  startLatitude?:  number | null;
  startLongitude?: number | null;
  /** Latest GPS the staff pushed via "Update location" during the shift (null until they do). */
  lastLatitude?:   number | null;
  lastLongitude?:  number | null;
  lastLocationAt?: string | null;
}

export interface RecentEventDto {
  staffName:    string;
  role:         string;
  locationName: string;
  timestamp:    string;
  eventType:    'shift-started' | 'shift-ended';
  notes?:       string | null;
}

export interface DashboardResponse {
  stats:           DashboardStats;
  currentlyOnShift: ActiveStaffDto[];
  recentActivity:  RecentEventDto[];
}

export interface ReportSummary {
  totalShifts:      number;
  totalHours:       number;
  totalCost:        number;
  uniqueStaff:      number;
  uniqueLocations:  number;
  avgHoursPerShift: number;
}

export interface StaffReportRow {
  staffId:         string;
  name:            string;
  role:            string;
  shifts:          number;
  hours:           number;
  cost:            number;
  avgHoursPerShift: number;
}

export interface LocationReportRow {
  locationName: string;
  shifts:       number;
  hours:        number;
  cost:         number;
}

export interface RoleReportRow {
  role:       string;
  shifts:     number;
  hours:      number;
  cost:       number;
  staffCount: number;
}

export interface DateReportRow {
  date:   string;
  shifts: number;
  hours:  number;
  cost:   number;
}

export interface ShiftDetailRow {
  id:           string;
  staffId:      string;
  staffName:    string;
  role:         string;
  locationId:   string;
  locationName: string;
  workDate:     string;        // yyyy-MM-dd
  startTime:    string;        // HH:mm
  endDate:      string | null; // yyyy-MM-dd (null = active; may differ from workDate for overnight/multi-day)
  endTime:      string | null; // HH:mm
  notes:        string | null;
  isEdited:     boolean;
  clockedIn:    string;
  clockedOut:   string | null;
  hours:        number;
  cost:         number;
  status:       string;
}

export interface ReportResponse {
  summary:    ReportSummary;
  byStaff:    StaffReportRow[];
  byLocation: LocationReportRow[];
  byRole:     RoleReportRow[];
  byDate:     DateReportRow[];
  shifts:     ShiftDetailRow[];
}

export interface ReportFilters {
  dateFrom:       string;
  dateTo:         string;
  staffIds?:      string[];
  locationNames?: string[];
  roles?:         string[];
}

export interface PayrollStaffRow {
  staffId: string;
  name: string;
  totalHours: number;
  totalPay: number;       // total hours × hourly rate; all leave is unpaid, so this is the full pay
}

export interface PayrollSummaryResponse {
  year: number;
  month: number;
  staff: PayrollStaffRow[];
  totalGross: number;
}

export interface StaffEarningsRow {
  staffId: string;
  name: string;
  earnedToday: number;
  earnedThisMonth: number;
  outstandingAdvance: number;   // unsettled cash advances owed, to deduct from salary
}

export interface EarningsSnapshotResponse {
  date: string;    // today, yyyy-MM-dd
  month: string;   // current month, yyyy-MM
  totalToday: number;
  totalThisMonth: number;
  totalOutstandingAdvance: number;
  staff: StaffEarningsRow[];
}

/** One completed shift on a staff member's detailed payslip. */
export interface PayslipShiftRow {
  workDate: string;             // yyyy-MM-dd
  startTime: string;            // HH:mm
  endDate: string | null;       // yyyy-MM-dd (may differ for overnight / multi-day)
  endTime: string | null;       // HH:mm
  hours: number;
  role: string;                 // job name
  locationName: string;
  payType: 'hourly' | 'fixed';
  rate: number;                 // per-hour, or flat-per-shift when fixed
  pay: number;                  // computed pay for this shift
  isEdited: boolean;
  notes: string | null;
}

/** Per-job subtotal on a payslip (shifts grouped by job + pay model). */
export interface PayslipJobSummary {
  role: string;
  payType: 'hourly' | 'fixed';
  shiftCount: number;
  hours: number;
  pay: number;
}

export interface PayslipResponse {
  staffId: string;
  staffName: string;
  from: string;                 // yyyy-MM-dd
  to: string;                   // yyyy-MM-dd
  totalHours: number;
  totalPay: number;
  outstandingAdvance: number;   // all unsettled cash advances (running balance)
  netPayable: number;           // max(0, totalPay − outstandingAdvance) — what to actually pay out
  jobBreakdown: PayslipJobSummary[];
  shifts: PayslipShiftRow[];
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  getDashboard(): Observable<DashboardResponse> {
    return this.http.get<DashboardResponse>('/api/admin/dashboard');
  }

  getReport(filters: ReportFilters): Observable<ReportResponse> {
    let params = new HttpParams()
      .set('dateFrom', filters.dateFrom)
      .set('dateTo',   filters.dateTo);

    for (const id  of (filters.staffIds      ?? [])) params = params.append('staffIds',      id);
    for (const loc of (filters.locationNames ?? [])) params = params.append('locationNames', loc);
    for (const r   of (filters.roles         ?? [])) params = params.append('roles',         r);

    return this.http.get<ReportResponse>('/api/admin/report', { params });
  }

  /** Download payroll CSV for a given year/month. Triggers browser download. */
  exportPayrollCsv(year: number, month: number): Observable<Blob> {
    return this.http.get('/api/admin/payroll/export',
      { params: new HttpParams().set('year', year).set('month', month), responseType: 'blob' }
    );
  }

  getPayrollSummary(year: number, month: number): Observable<PayrollSummaryResponse> {
    return this.http.get<PayrollSummaryResponse>('/api/admin/payroll/summary',
      { params: new HttpParams().set('year', year).set('month', month) });
  }

  /** Live per-staff "earned today" + "earned this month" totals (completed shifts). */
  getEarningsSnapshot(): Observable<EarningsSnapshotResponse> {
    return this.http.get<EarningsSnapshotResponse>('/api/admin/payroll/earnings-snapshot');
  }

  /** Detailed payslip for one staff member: every completed shift between `from`–`to` (yyyy-MM-dd). */
  getPayslip(staffId: string, from: string, to: string): Observable<PayslipResponse> {
    return this.http.get<PayslipResponse>('/api/admin/payroll/payslip',
      { params: new HttpParams().set('staffId', staffId).set('from', from).set('to', to) });
  }

  /** Download the detailed payslip as CSV. Triggers a browser download. */
  exportPayslipCsv(staffId: string, from: string, to: string): Observable<Blob> {
    return this.http.get('/api/admin/payroll/payslip/export',
      { params: new HttpParams().set('staffId', staffId).set('from', from).set('to', to), responseType: 'blob' });
  }
}
