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
}
