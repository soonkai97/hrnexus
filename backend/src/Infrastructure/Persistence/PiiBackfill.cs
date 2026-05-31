using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Infrastructure.Persistence;

/// <summary>
/// One-time, idempotent encryption of pre-existing plaintext PII. The field converters read legacy
/// (unmarked) values as-is, so old rows stay usable; this re-saves them so they're encrypted at rest.
/// Guarded by a marker check ("enc1:") so it runs only while unencrypted rows remain — once all rows
/// are encrypted it's a no-op on every subsequent startup.
/// </summary>
public static class PiiBackfill
{
    public static async Task EncryptExistingAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return;

        // Detect rows whose PII columns hold non-null, non-encrypted (no "enc1:" marker) values.
        var pending = await db.Database
            .SqlQueryRaw<long>(
                @"SELECT COUNT(*) AS ""Value"" FROM ""Staff"" WHERE
                    (""IcNo""        IS NOT NULL AND ""IcNo""        NOT LIKE 'enc1:%') OR
                    (""PassportNo""  IS NOT NULL AND ""PassportNo""  NOT LIKE 'enc1:%') OR
                    (""PhoneNumber"" IS NOT NULL AND ""PhoneNumber"" NOT LIKE 'enc1:%') OR
                    (""DateOfBirth"" IS NOT NULL AND ""DateOfBirth"" NOT LIKE 'enc1:%')")
            .SingleAsync(ct);
        if (pending == 0) return;

        // Load every staff row (incl. soft-deleted) and force the PII columns to be rewritten — the
        // value converters encrypt them on the way out.
        var all = await db.Staff.IgnoreQueryFilters().ToListAsync(ct);
        foreach (var s in all)
        {
            var entry = db.Entry(s);
            entry.Property(nameof(Staff.IcNo)).IsModified = true;
            entry.Property(nameof(Staff.PassportNo)).IsModified = true;
            entry.Property(nameof(Staff.PhoneNumber)).IsModified = true;
            entry.Property(nameof(Staff.DateOfBirth)).IsModified = true;
        }
        await db.SaveChangesAsync(ct);
    }
}
