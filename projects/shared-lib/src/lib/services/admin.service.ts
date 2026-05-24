import { Injectable } from '@angular/core';
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
  staffId:        string;
  name:           string;
  role:           string;
  locationName:   string;
  clockedInAt:    string;
  minutesOnShift: number;
}

export interface RecentEventDto {
  staffName:    string;
  role:         string;
  locationName: string;
  timestamp:    string;
  eventType:    'shift-started' | 'shift-ended';
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
  endTime:      string | null; // HH:mm
  notes:        string | null;
  isEdited:     boolean;
  clockedIn:    string;
  clockedOut:   string | null;
  hours:        number;
  cost:         number;
  status:       string;
  isOvertime:   boolean;
  otThreshold:  number;
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

export interface TipsPoolDto { id: string; locationName: string; shiftDate: string; totalAmount: number; splitMethod: string; isDistributed: boolean; staffCount: number; createdAt: string; }
export interface TipsAllocation { employeeId: string; fullName: string; amount: number; hoursWorked: number; }
export interface TipsExcludedStaff { employeeId: string; fullName: string; reason: string; }
export interface TipsDistributionResult { staffCount: number; allocations: TipsAllocation[]; excludedStaff: TipsExcludedStaff[]; }

export interface PayrollStaffRow {
  staffId: string;
  name: string;
  regularHours: number;
  overtimeHours: number;
  totalPay: number;       // work-hours pay (reg + OT)
  paidLeaveDays: number;
  paidLeavePay: number;
  grandTotal: number;     // totalPay + paidLeavePay
}

export interface PayrollSummaryResponse {
  year: number;
  month: number;
  staff: PayrollStaffRow[];
  totalGross: number;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  constructor(private http: HttpClient) {}

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

  getTipsPools(dateFrom?: string, dateTo?: string): Observable<TipsPoolDto[]> {
    const q = new URLSearchParams();
    if (dateFrom) q.set('dateFrom', dateFrom);
    if (dateTo) q.set('dateTo', dateTo);
    return this.http.get<TipsPoolDto[]>(`/api/admin/tips-pools?${q}`);
  }

  distributeTips(dto: any): Observable<TipsDistributionResult> {
    return this.http.post<TipsDistributionResult>('/api/admin/tips-pools/distribute', dto);
  }

  getPayrollSummary(year: number, month: number): Observable<PayrollSummaryResponse> {
    return this.http.get<PayrollSummaryResponse>('/api/admin/payroll/summary',
      { params: new HttpParams().set('year', year).set('month', month) });
  }
}
