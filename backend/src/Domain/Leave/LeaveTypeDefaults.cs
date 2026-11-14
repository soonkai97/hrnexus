using HRNexus.Domain.Entities;

namespace HRNexus.Domain.Leave;

/// <summary>
/// The leave types an untouched installation has, until an administrator saves a catalogue of their own.
/// This is the ONE place that names the built-in types: everything else asks the catalogue what a type
/// does (a guard test fails if a handler compares a type's name).
///
/// The first five (annual, emergency, sick, unpaid, no-show) behave exactly as the product did before
/// leave types became data. The statutory figures on the four newer types are ASSUMED (general knowledge of
/// the Employment Act and common practice), not confirmed for any company: counsel or the accountant checks
/// them and the company changes them on the policy page.
/// </summary>
public static class LeaveTypeDefaults
{
    public const string AnnualKey          = "annual";
    public const string EmergencyKey       = "emergency";
    /// <summary>System type: the sick-pattern warnings and the sick pool depend on it.</summary>
    public const string SickKey            = "sick";
    public const string HospitalisationKey = "hospitalisation";
    public const string MaternityKey       = "maternity";
    public const string PaternityKey       = "paternity";
    public const string CompassionateKey   = "compassionate";
    public const string UnpaidKey          = "unpaid";
    /// <summary>System type: how an administrator records leave that was booked but not taken.</summary>
    public const string NoShowKey          = "no-show";

    /// <summary>Fresh instances every call: the entities are mutable, so callers must never share them.</summary>
    public static IReadOnlyList<LeaveTypeDefinition> All() =>
    [
        Make(AnnualKey, "Annual", "success", 10, LeavePools.Annual, advanceNoticeFromPolicy: true),
        Make(EmergencyKey, "Emergency", "warn", 20, LeavePools.Annual, autoApprove: true),
        Make(SickKey, "Sick", "danger", 30, LeavePools.Sick, autoApprove: true, isSystem: true),
        Make(HospitalisationKey, "Hospitalisation", "danger", 40, LeavePools.Own, allowanceDays: 60m, sensitive: true),
        Make(MaternityKey, "Maternity", "info", 50, LeavePools.None, maxDays: 98m, calendarDays: true, sensitive: true),
        Make(PaternityKey, "Paternity", "info", 60, LeavePools.None, maxDays: 7m, calendarDays: true, sensitive: true),
        Make(CompassionateKey, "Compassionate", "secondary", 70, LeavePools.None, maxDays: 3m, sensitive: true),
        Make(UnpaidKey, "Unpaid", "secondary", 80, LeavePools.None, deductsPay: true),
        Make(NoShowKey, "No-show", "warn", 90, LeavePools.None, deductsPay: true, adminOnly: true, isSystem: true),
    ];

    private static LeaveTypeDefinition Make(string key, string label, string colour, int sort, string pool,
        decimal? allowanceDays = null, decimal? maxDays = null, bool autoApprove = false, bool advanceNoticeFromPolicy = false,
        bool deductsPay = false, bool adminOnly = false, bool calendarDays = false, bool sensitive = false, bool isSystem = false) =>
        LeaveTypeDefinition.Create(new LeaveTypeSpec(key, label, colour, sort, Active: true, pool, allowanceDays, maxDays,
            autoApprove, AdvanceNoticeDays: null, advanceNoticeFromPolicy, deductsPay, adminOnly, calendarDays, sensitive),
            isSystem);
}
