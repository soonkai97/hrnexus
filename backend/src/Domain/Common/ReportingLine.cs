namespace HRNexus.Domain.Common;

/// <summary>Rules about who reports to whom. Pure: it is handed the current manager of every person.</summary>
public static class ReportingLine
{
    /// <summary>
    /// True when making <paramref name="managerId"/> the manager of <paramref name="staffId"/> would create a
    /// loop (a person above themselves). Walks up from the new manager; a chain that already loops is treated as
    /// a loop too, so the walk always ends.
    /// </summary>
    public static bool WouldLoop(Guid staffId, Guid? managerId, IReadOnlyDictionary<Guid, Guid?> managerOf)
    {
        var seen = new HashSet<Guid>();
        var current = managerId;
        while (current is { } id)
        {
            if (id == staffId || !seen.Add(id)) return true;
            current = managerOf.TryGetValue(id, out var next) ? next : null;
        }
        return false;
    }
}
