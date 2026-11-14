using System.Text.RegularExpressions;
using HRNexus.Domain.Leave;
using HRNexus.Domain.Policy;

namespace HRNexus.Domain.Entities;

/// <summary>
/// One leave type in the company's catalogue (Phase 7q): how it is named, where its hours are charged and
/// which rules apply to it. A company's types are data (rules that differ between companies are settings,
/// not constants), so no handler compares a type's name; it asks the type's definition.
///
/// A type's <see cref="Key"/> is the value already stored on every <c>LeaveRequest</c>, so it never changes.
/// <see cref="Pool"/> and <see cref="CountsCalendarDays"/> are locked once a type has leave (changing them
/// would refund or recharge hours already booked). A type with leave is deactivated, never removed.
/// </summary>
public class LeaveTypeDefinition
{
    private static readonly Regex KeyPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    public string   Key                     { get; private set; } = default!;
    public string   Label                   { get; private set; } = default!;
    public string   Colour                  { get; private set; } = default!;
    public int      SortOrder               { get; private set; }
    public bool     Active                  { get; private set; }
    /// <summary>A locked system type (sick and no-show): its behaviour cannot change and it cannot be deactivated.</summary>
    public bool     IsSystem                { get; private set; }
    public string   Pool                    { get; private set; } = LeavePools.None;
    public decimal? AllowanceDays           { get; private set; }
    public decimal? MaxDaysPerRequest       { get; private set; }
    public bool     AutoApprove             { get; private set; }
    public int?     AdvanceNoticeDays       { get; private set; }
    public bool     AdvanceNoticeFromPolicy { get; private set; }
    public bool     DeductsPay              { get; private set; }
    public bool     AdminOnly               { get; private set; }
    public bool     CountsCalendarDays      { get; private set; }
    public bool     Sensitive               { get; private set; }
    /// <summary>Counts saves of the whole catalogue (every row carries the same value); a save made from a stale copy is refused.</summary>
    public int      CatalogVersion          { get; private set; }
    public DateTime UpdatedAt               { get; private set; }
    public string?  UpdatedBy               { get; private set; }

    private LeaveTypeDefinition() { }

    /// <exception cref="ArgumentException">Any value is outside its allowed range.</exception>
    public static LeaveTypeDefinition Create(LeaveTypeSpec spec, bool isSystem = false, string? createdBy = null)
    {
        var t = new LeaveTypeDefinition { IsSystem = isSystem, UpdatedAt = DateTime.UtcNow, UpdatedBy = createdBy };
        t.Apply(Validate(spec));
        return t;
    }

    /// <exception cref="ArgumentException">A value is outside its allowed range, or the key differs.</exception>
    /// <exception cref="InvalidOperationException">The change is locked (the type has leave, or it is a system type).</exception>
    public void Update(LeaveTypeSpec spec, bool hasLeave, string? updatedBy)
    {
        if (!string.Equals(spec.Key?.Trim(), Key, StringComparison.Ordinal))
            throw new ArgumentException("A leave type's key cannot be changed.");
        var s = Validate(spec);

        if (hasLeave && (s.Pool != Pool || s.CountsCalendarDays != CountsCalendarDays))
            throw new InvalidOperationException(
                $"'{Label}' already has leave, so where it is charged and how its days are counted can no longer change. " +
                "Deactivate it and add a new type instead.");

        if (IsSystem
            && (!s.Active || s.Pool != Pool || s.AutoApprove != AutoApprove || s.DeductsPay != DeductsPay
                || s.AdminOnly != AdminOnly || s.CountsCalendarDays != CountsCalendarDays))
            throw new InvalidOperationException(
                $"'{Label}' is a system type: its behaviour is fixed and it must stay active. You can rename or recolour it.");

        Apply(s);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// The schedule this type counts its days by: the company's working days, or every day of the week (at the
    /// company's hours per day) for a calendar-day type. Every place that turns dates into hours must use this,
    /// so a leave is the same hours everywhere.
    /// </summary>
    public WorkSchedule ScheduleFor(WorkSchedule company) =>
        CountsCalendarDays ? WorkSchedule.FromIsoDays([1, 2, 3, 4, 5, 6, 7], company.WorkdayHours) : company;

    /// <summary>Days of notice this type needs (the company policy's annual-leave notice, or its own days), or null for none.</summary>
    public int? NoticeDays(CompanyPolicy policy) => AdvanceNoticeFromPolicy ? policy.AnnualLeaveAdvanceDays : AdvanceNoticeDays;

    /// <summary>Stamps the catalogue version when a save commits (set on every row of the catalogue).</summary>
    public void SetCatalogVersion(int version) => CatalogVersion = version;

    /// <summary>The catalogue rules that span rows.</summary>
    /// <exception cref="ArgumentException">A key repeats, nothing can be applied for, or a system type is missing.</exception>
    public static void EnsureValidCatalogue(IReadOnlyCollection<LeaveTypeDefinition> types)
    {
        if (types.Count == 0)
            throw new ArgumentException("The catalogue needs at least one leave type.");
        if (types.Select(t => t.Key).Distinct().Count() != types.Count)
            throw new ArgumentException("Each leave type needs its own key.");
        foreach (var system in new[] { LeaveTypeDefaults.SickKey, LeaveTypeDefaults.NoShowKey })
            if (!types.Any(t => t.Key == system && t.IsSystem))
                throw new ArgumentException($"The '{system}' type is a system type and must stay in the catalogue.");
        if (!types.Any(t => t.Active && !t.AdminOnly))
            throw new ArgumentException("At least one active leave type must be open to staff.");
    }

    // ── internals ────────────────────────────────────────────────────────────────

    private static LeaveTypeSpec Validate(LeaveTypeSpec s)
    {
        var key = s.Key?.Trim() ?? "";
        if (key.Length is 0 or > 20 || !KeyPattern.IsMatch(key))
            throw new ArgumentException("A leave type's key is 1 to 20 lower-case letters, digits or hyphens (for example 'study-leave').");
        var label = s.Label?.Trim() ?? "";
        if (label.Length is 0 or > 40)
            throw new ArgumentException("A leave type needs a name of up to 40 characters.");
        if (!LeaveColours.All.Contains(s.Colour ?? ""))
            throw new ArgumentException($"The colour must be one of: {string.Join(", ", LeaveColours.All)}.");
        if (!LeavePools.All.Contains(s.Pool ?? ""))
            throw new ArgumentException($"Where the leave is charged must be one of: {string.Join(", ", LeavePools.All)}.");

        if (s.Pool == LeavePools.Own)
        {
            if (s.AllowanceDays is null or <= 0 or > 366)
                throw new ArgumentException($"'{label}' has its own yearly allowance: enter between 0.5 and 366 days.");
        }
        else if (s.AllowanceDays is not null)
            throw new ArgumentException($"'{label}' does not have its own allowance; only a type with its own pool does.");

        if (s.MaxDaysPerRequest is not null && (s.MaxDaysPerRequest < 0.5m || s.MaxDaysPerRequest > 366m))
            throw new ArgumentException("The longest single request must be between 0.5 and 366 days.");
        if (s.AdvanceNoticeDays is < 0 or > 60)
            throw new ArgumentException("Notice must be between 0 and 60 days.");
        if (s.AdvanceNoticeFromPolicy && s.AdvanceNoticeDays is not null)
            throw new ArgumentException("Notice comes either from the company policy or from this type, not both.");
        if (s.AutoApprove && s.Pool == LeavePools.None)
            throw new ArgumentException($"'{label}' has no balance, so it cannot approve itself: a long absence needs a manager's decision.");
        if (s.AutoApprove && s.AdminOnly)
            throw new ArgumentException($"'{label}' is recorded by an administrator, so it cannot also approve itself.");
        if (s.SortOrder is < 0 or > 1000)
            throw new ArgumentException("The sort order must be between 0 and 1000.");

        return s with { Key = key, Label = label };
    }

    private void Apply(LeaveTypeSpec s)
    {
        Key                     = s.Key;
        Label                   = s.Label;
        Colour                  = s.Colour;
        SortOrder               = s.SortOrder;
        Active                  = s.Active;
        Pool                    = s.Pool;
        AllowanceDays           = s.AllowanceDays;
        MaxDaysPerRequest       = s.MaxDaysPerRequest;
        AutoApprove             = s.AutoApprove;
        AdvanceNoticeDays       = s.AdvanceNoticeFromPolicy ? null : s.AdvanceNoticeDays;
        AdvanceNoticeFromPolicy = s.AdvanceNoticeFromPolicy;
        DeductsPay              = s.DeductsPay;
        AdminOnly               = s.AdminOnly;
        CountsCalendarDays      = s.CountsCalendarDays;
        Sensitive               = s.Sensitive;
    }
}
