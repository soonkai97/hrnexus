namespace HRNexus.Domain.Leave;

/// <summary>
/// What an administrator chooses for one leave type (the editable part of a <c>LeaveTypeDefinition</c>).
/// Both creating and editing a type take one, so the rules live in one place.
/// </summary>
/// <param name="Key">Immutable slug (lower-case letters, digits, hyphens, at most 20); the value stored on every leave request.</param>
/// <param name="Label">The name people see.</param>
/// <param name="Colour">One of <see cref="LeaveColours.All"/>.</param>
/// <param name="SortOrder">Order in lists; lower first.</param>
/// <param name="Active">Staff can apply for it; an inactive type keeps its old leave but takes no new requests.</param>
/// <param name="Pool">annual | sick | own | none (see <see cref="LeavePools"/>).</param>
/// <param name="AllowanceDays">Yearly allowance for an <c>own</c> pool, in days; null for every other pool.</param>
/// <param name="MaxDaysPerRequest">Longest single request in calendar days; replaces the company policy cap for this type. Null keeps the policy cap.</param>
/// <param name="AutoApprove">Applying approves it at once. Only allowed with a balance (a pool).</param>
/// <param name="AdvanceNoticeDays">Days of notice needed; null = none. Ignored when <paramref name="AdvanceNoticeFromPolicy"/>.</param>
/// <param name="AdvanceNoticeFromPolicy">The notice is the company policy's annual-leave notice (one place to change it).</param>
/// <param name="DeductsPay">Payroll deducts pay for this leave (unpaid, no-show).</param>
/// <param name="AdminOnly">Only an administrator can record it; staff cannot apply for it.</param>
/// <param name="CountsCalendarDays">Counts consecutive calendar days (weekends included) instead of the company's working days.</param>
/// <param name="Sensitive">Notifications, push messages and the withdrawal notice say "leave", never the type.</param>
public sealed record LeaveTypeSpec(
    string  Key,
    string  Label,
    string  Colour,
    int     SortOrder,
    bool    Active,
    string  Pool,
    decimal? AllowanceDays,
    decimal? MaxDaysPerRequest,
    bool    AutoApprove,
    int?    AdvanceNoticeDays,
    bool    AdvanceNoticeFromPolicy,
    bool    DeductsPay,
    bool    AdminOnly,
    bool    CountsCalendarDays,
    bool    Sensitive);

/// <summary>Where a leave type's hours are charged.</summary>
public static class LeavePools
{
    /// <summary>The person's yearly annual entitlement (shared by every type in this pool, e.g. annual and emergency).</summary>
    public const string Annual = "annual";
    /// <summary>The person's yearly sick entitlement.</summary>
    public const string Sick = "sick";
    /// <summary>A company-wide yearly allowance (days) that belongs to this one type.</summary>
    public const string Own = "own";
    /// <summary>No balance: limited only by the type's day limit and a manager's approval.</summary>
    public const string None = "none";

    public static readonly IReadOnlyList<string> All = [Annual, Sick, Own, None];
}

/// <summary>The fixed set of tag colours a type can use (they map to the UI's tag severities).</summary>
public static class LeaveColours
{
    public static readonly IReadOnlyList<string> All = ["info", "success", "warn", "danger", "secondary", "contrast"];
}
