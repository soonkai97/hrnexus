namespace HRNexus.Domain.Constants;

/// <summary>
/// Central registry of magic strings and numeric limits used across the application.
/// Centralising them here eliminates scattered literals and makes cross-layer changes
/// a single-file edit that the compiler will catch at every call-site.
/// </summary>
public static class AppConstants
{
    // ── Permissions ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Permission catalog — the named capabilities that gate each management
    /// feature. Access is resolved from these (role defaults + per-user overrides), NOT from
    /// roles directly. One key per existing controller/feature gate.
    /// </summary>
    public static class Permissions
    {
        public const string StaffView          = "staff.view";          // view staff list/details
        public const string StaffManage        = "staff.manage";        // create/edit/deactivate staff
        public const string LeaveApprove       = "leave.approve";       // approve/reject leave
        public const string LeaveManage        = "leave.manage";        // entitlements / leave admin
        public const string PolicyManage       = "policy.manage";       // company policy: working week, leave rules
        public const string ReportsView        = "reports.view";        // workforce/leave reports
        public const string PayrollView        = "payroll.view";        // view payroll
        public const string PayrollManage      = "payroll.manage";      // run/export payroll
        public const string PayrollLock        = "payroll.lock";        // lock/reopen payroll periods
        public const string RolesManage        = "roles.manage";        // roles + rates + permissions
        public const string CashAdvanceManage  = "cashadvance.manage";  // record/settle staff cash advances

        /// <summary>Every catalog key — used for validation and seeding system roles.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            StaffView, StaffManage, LeaveApprove, LeaveManage, PolicyManage,
            ReportsView, PayrollView, PayrollManage, PayrollLock,
            RolesManage, CashAdvanceManage,
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

        /// <summary>Hours in one working day (Monday to Friday); leave is charged in these units.</summary>
        public const decimal WorkdayHours = 8m;

        /// <summary>Default for the longest single leave request (days); the company policy can change it.</summary>
        public const int MaxLeaveDays = 30;
    }

    // ── Company policy defaults ────────────────────────────────────────────────
    public static class Policy
    {
        public const string CarryOverNone      = "none";
        public const string CarryOverCapped    = "capped";
        public const string CarryOverUnlimited = "unlimited";

        public const decimal DefaultCarryOverMaxHours  = 14m;
        public const decimal DefaultUnpaidLeaveDivisor = 26m;
    }

    // ── Payroll ────────────────────────────────────────────────────────────────
    public static class Payroll
    {
        public const string Monthly           = "monthly";
        public const string Citizen           = "citizen";
        public const string PermanentResident = "permanent-resident";

        /// <summary>Sanity ceiling for one person's monthly basic salary (MYR); rejects typos like an extra zero.</summary>
        public const decimal MaxMonthlySalary = 1_000_000m;
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
        public const int PushSubscriptionsCap = 5;

        // Pagination
        public const int MaxPageSize = 100;
    }
}
