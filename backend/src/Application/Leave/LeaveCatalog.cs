using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Leave;
using HRNexus.Domain.Policy;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Leave;

/// <summary>
/// The company's leave types as the leave handlers see them (Phase 7q). A handler never compares a type's
/// name: it finds the type's definition here and asks what the type does.
/// </summary>
public sealed class LeaveCatalog
{
    private readonly Dictionary<string, LeaveTypeDefinition> _byKey;

    public LeaveCatalog(IEnumerable<LeaveTypeDefinition> types, bool isSaved)
    {
        Types = types.OrderBy(t => t.SortOrder).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
        _byKey = Types.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        IsSaved = isSaved;
    }

    /// <summary>Every type in the catalogue (active or not), in sort order.</summary>
    public IReadOnlyList<LeaveTypeDefinition> Types { get; }

    /// <summary>True when an administrator has saved a catalogue; false means the code defaults apply.</summary>
    public bool IsSaved { get; }

    /// <summary>The type with this key (any letter case), or null when the catalogue does not have it.</summary>
    public LeaveTypeDefinition? Find(string key) => _byKey.GetValueOrDefault(key ?? "");

    /// <summary>
    /// The keys of every type charged to the same balance as <paramref name="type"/>: types in the annual or
    /// sick pool share it, an own-pool type is its own pool, and a type with no balance has none (empty).
    /// </summary>
    public IReadOnlyList<string> PoolKeys(LeaveTypeDefinition type) => type.Pool switch
    {
        LeavePools.Annual or LeavePools.Sick => Types.Where(t => t.Pool == type.Pool).Select(t => t.Key).ToList(),
        LeavePools.Own => [type.Key],
        _ => [],
    };

    /// <summary>
    /// The schedule a stored leave type counts its days by (see <see cref="LeaveTypeDefinition.ScheduleFor"/>);
    /// a key the catalogue no longer has counts the company's working days.
    /// </summary>
    public WorkSchedule ScheduleFor(string key, WorkSchedule company) => Find(key)?.ScheduleFor(company) ?? company;

    /// <summary>
    /// "annual leave" for an ordinary type, plain "leave" for a sensitive one: used in every message that
    /// reaches another person or a lock screen, so health and family facts are not broadcast.
    /// A key that is no longer in the catalogue is shown as stored.
    /// </summary>
    public string Phrase(string key) => Find(key) switch
    {
        { Sensitive: true } => "leave",
        { } t => t.Label.EndsWith("leave", StringComparison.OrdinalIgnoreCase) ? t.Label.ToLowerInvariant() : $"{t.Label.ToLowerInvariant()} leave",
        _ => $"{key} leave",
    };
}

/// <summary>
/// Reads the leave types. Until an administrator saves a catalogue the code defaults apply, so an untouched
/// installation behaves exactly as it did before leave types became data (the same way the company policy
/// falls back to its defaults).
/// </summary>
public static class LeaveCatalogReader
{
    public static async Task<LeaveCatalog> GetAsync(IAppDbContext db, CancellationToken ct)
    {
        var saved = await db.LeaveTypes.AsNoTracking().ToListAsync(ct);
        return saved.Count > 0
            ? new LeaveCatalog(saved, isSaved: true)
            : new LeaveCatalog(LeaveTypeDefaults.All(), isSaved: false);
    }
}
