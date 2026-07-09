namespace HRNexus.Domain.Constants;

/// <summary>
/// Central registry of magic strings and numeric limits used across the application.
/// Centralising them here eliminates scattered literals and makes cross-layer changes
/// a single-file edit that the compiler will catch at every call-site.
/// </summary>
public static class AppConstants
{
    // ── Roles ──────────────────────────────────────────────────────────────────
    public static class Roles
    {
        public const string Admin    = "SUPERADMIN";
        public const string Manager  = "MANAGER";
        public const string Employee = "EMPLOYEE";

        /// <summary>JWT claim value for the highest-privilege tier.</summary>
        public const string AdminClaim   = "SuperAdmin";
        public const string ManagerClaim = "Manager";

        /// <summary>Comma-separated claim string for use in [Authorize(Roles = ...)] attributes.</summary>
        public const string ManagerOrAdminClaim = ManagerClaim + "," + AdminClaim;
    }

    // ── Permissions ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Permission catalog — the named capabilities that gate portal access and each admin
    /// feature. Access is resolved from these (role defaults + per-user overrides), NOT from
    /// roles directly. One key per existing controller/feature gate.
    /// </summary>
    public static class Permissions
    {
        public const string PortalAdmin        = "portal.admin";        // sign into the admin portal
        public const string StaffView          = "staff.view";          // view staff list/details
        public const string StaffManage        = "staff.manage";        // create/edit/deactivate staff
        public const string LeaveApprove       = "leave.approve";       // approve/reject leave
        public const string LeaveManage        = "leave.manage";        // entitlements / leave admin
        public const string WorkLogManage      = "worklog.manage";      // edit others' shifts, approve
        public const string ReportsView        = "reports.view";        // workforce/leave reports
        public const string PayrollView        = "payroll.view";        // view payroll
        public const string PayrollManage      = "payroll.manage";      // run/export payroll
        public const string PayrollLock        = "payroll.lock";        // lock/reopen payroll periods
        public const string RolesManage        = "roles.manage";        // roles + rates + permissions
        public const string LocationsManage    = "locations.manage";    // venue locations
        public const string CashAdvanceManage  = "cashadvance.manage";  // record/settle staff cash advances

        /// <summary>Every catalog key — used for validation and seeding system roles.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            PortalAdmin, StaffView, StaffManage, LeaveApprove, LeaveManage, WorkLogManage,
            ReportsView, PayrollView, PayrollManage, PayrollLock,
            RolesManage, LocationsManage, CashAdvanceManage,
        };

        /// <summary>Authorization policy-name prefix: [Authorize(Policy = "perm:staff.manage")].</summary>
        public const string PolicyPrefix = "perm:";
    }

    // ── Leave ──────────────────────────────────────────────────────────────────
    public static class Leave
    {
        // Status values
        public const string Pending   = "pending";
        public const string Approved  = "approved";
        public const string Rejected  = "rejected";
        public const string Cancelled = "cancelled";

        // Type values
        public const string Sick      = "sick";
        public const string Annual    = "annual";
        public const string Unpaid    = "unpaid";
        public const string NoShow    = "no-show";
        public const string Emergency = "emergency";

        /// <summary>Number of calendar days ahead that annual leave must be applied.</summary>
        public const int AnnualLeaveAdvanceDays = 3;
    }

    // ── Pay types ────────────────────────────────────────────────────────────────
    /// <summary>How a job is paid. "hourly" = hours × rate; "fixed" = a flat amount per shift
    /// (e.g. an overnight package) regardless of hours logged.</summary>
    public static class PayType
    {
        public const string Hourly = "hourly";
        public const string Fixed  = "fixed";
    }

    // ── Shift limits ─────────────────────────────────────────────────────────────
    public static class WorkLog
    {
        /// <summary>Default single-shift length, in days, for a newly-created job and the fallback when a
        /// shift's job can't be resolved. Each job carries its own <c>Role.MaxShiftDays</c> (configurable
        /// 1..<see cref="MaxShiftDaysCeiling"/>) that the shift-capture flows enforce. Shifts may span
        /// multiple days (some jobs run continuously), but an end date beyond the job's cap is rejected
        /// across clock-out, manual entry, and admin close/edit.</summary>
        public const int MaxShiftDays = 30;

        /// <summary>Hard upper bound a job's <c>MaxShiftDays</c> may be configured to — a sanity ceiling
        /// so the per-job typo guard can't be set arbitrarily high.</summary>
        public const int MaxShiftDaysCeiling = 100;
    }

    // ── Notification types ─────────────────────────────────────────────────────
    public static class NotificationType
    {
        public const string LeaveApproved  = "leave_approved";
        public const string LeaveRejected  = "leave_rejected";
        public const string LeavePending   = "leave_pending";
    }

    // ── Auth cookies ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Names of the httpOnly auth cookies. Tokens live ONLY in these cookies (never in the JSON
    /// body or localStorage) so XSS can't exfiltrate them. Referenced by both the API controller
    /// (which writes them) and the JWT bearer setup in Infrastructure (which reads the access cookie).
    /// </summary>
    public static class AuthCookies
    {
        public const string AccessToken  = "hrnexus_at";
        public const string RefreshToken = "hrnexus_rt";
    }

    // ── Brute-force lockout ──────────────────────────────────────────────────────
    public static class Lockout
    {
        /// <summary>Consecutive failed logins that trigger an account lockout.</summary>
        public const int MaxFailedAttempts = 5;

        /// <summary>How long an account stays locked once the threshold is hit.</summary>
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);
    }

    // ── Validation limits ──────────────────────────────────────────────────────
    public static class Limits
    {
        // Staff / authentication
        public const int PasswordMinLength     = 8;
        public const int StaffIdMaxLength      = 20;
        public const int NameMaxLength         = 100;
        public const int EmailMaxLength        = 254;
        public const int PhoneMaxLength        = 20;
        public const int PushSubscriptionsCap  = 5;

        // Notifications
        public const int NotificationSnippetLength = 80;

        // Locations
        public const int LocationNameMaxLength = 100;
        public const long ImageMaxBytes        = 5_242_880; // 5 MB

        // Pagination
        public const int DefaultPageSize = 20;
        public const int MaxPageSize     = 100;
    }
}
