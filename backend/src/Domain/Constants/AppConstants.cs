namespace NightclubWorkforce.Domain.Constants;

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
        public const string Admin    = "ADMIN";
        public const string Manager  = "MANAGER";
        public const string Employee = "EMPLOYEE";

        /// <summary>JWT claim value for the highest-privilege tier.</summary>
        public const string AdminClaim   = "Admin";
        public const string ManagerClaim = "Manager";

        /// <summary>Comma-separated claim string for use in [Authorize(Roles = ...)] attributes.</summary>
        public const string ManagerOrAdminClaim = ManagerClaim + "," + AdminClaim;
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
        public const string Sick   = "sick";
        public const string Annual = "annual";
        public const string Unpaid = "unpaid";
        public const string NoShow = "no-show";
    }

    // ── Notification types ─────────────────────────────────────────────────────
    public static class NotificationType
    {
        public const string LeaveApproved  = "leave_approved";
        public const string LeaveRejected  = "leave_rejected";
        public const string LeavePending   = "leave_pending";
        public const string MissedClockIn  = "missed_clockin";
        public const string RosterAssigned = "roster_assigned";
        public const string Message        = "message";
        public const string Announcement   = "announcement";
        public const string Tips           = "tips";
    }

    // ── Tips split methods ─────────────────────────────────────────────────────
    public static class TipsSplitMethod
    {
        public const string Equal   = "equal";
        public const string ByHours = "by_hours";
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

        // Messages
        public const int MessageMaxLength      = 2000;
        public const int NotificationSnippetLength = 80;

        // Announcements
        public const int AnnouncementTitleMaxLength = 200;
        public const int AnnouncementBodyMaxLength  = 5_000;

        // Tips
        public const decimal TipsMaxAmount = 50_000m;

        // Locations
        public const int LocationNameMaxLength = 100;
        public const long ImageMaxBytes        = 5_242_880; // 5 MB

        // Pagination
        public const int DefaultPageSize = 20;
        public const int MaxPageSize     = 100;

        // Work-log / overtime
        public const int OvertimeThresholdHours = 12;
    }

    // ── Feature flag keys ──────────────────────────────────────────────────────
    public static class FeatureFlags
    {
        public const string Tips          = "tips";
        public const string TipsHistory   = "tipsHistory";
        public const string Payroll       = "payroll";
        public const string PaySummary    = "paySummary";
        public const string Announcements = "announcements";
        public const string StaffLedger = "staffLedger";
    }
}
