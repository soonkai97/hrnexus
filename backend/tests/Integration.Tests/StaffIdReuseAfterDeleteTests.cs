using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Proves that a Staff ID (and email) can be REUSED after the previous holder is soft-deleted —
/// against real PostgreSQL, because only a real DB enforces the partial unique index. Delete is a
/// soft-delete (IsDeleted=true), and the unique indexes on StaffId/Email are filtered to
/// <c>"IsDeleted" = false</c>, so a deleted row no longer occupies the ID. Before this fix the
/// deleted row collided on a non-partial unique index and the register handler mislabeled it as
/// "Email '' is already registered."
/// </summary>
[Collection("postgres")]
public class StaffIdReuseAfterDeleteTests(PostgresFixture fx)
{
    [Fact]
    public async Task StaffId_CanBeReused_AfterPreviousHolderIsSoftDeleted()
    {
        var staffId = $"RU{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        // Create then soft-delete the first holder.
        await using (var db = fx.NewDb())
        {
            var first = E.Staff.Create(staffId, "First", "Holder", "first@t.com", "hash", new List<Guid>());
            db.Staff.Add(first);
            await db.SaveChangesAsync();

            db.Staff.Remove(first);           // interceptor converts to IsDeleted = true
            await db.SaveChangesAsync();
        }

        // A brand-new staff member reusing the SAME id must insert cleanly — the partial unique
        // index ignores the soft-deleted row. (Would throw DbUpdateException before the fix.)
        Guid newId;
        await using (var db = fx.NewDb())
        {
            var second = E.Staff.Create(staffId, "Second", "Holder", "second@t.com", "hash", new List<Guid>());
            db.Staff.Add(second);
            await db.SaveChangesAsync();      // must not throw
            newId = second.Id;
        }

        // Exactly one ACTIVE staff holds the id, and it's the new record (not the deleted one).
        await using (var verify = fx.NewDb())
        {
            var active = await verify.Staff.Where(s => s.StaffId == staffId).ToListAsync();   // filtered = non-deleted
            Assert.Single(active);
            Assert.Equal(newId, active[0].Id);
            Assert.Equal("Second", active[0].FirstName);
        }
    }

    [Fact]
    public async Task Email_CanBeReused_AfterPreviousHolderIsSoftDeleted()
    {
        var email = $"reuse{Guid.NewGuid():N}@t.com";

        await using (var db = fx.NewDb())
        {
            var first = E.Staff.Create($"E1{Guid.NewGuid():N}"[..10], "First", "Holder", email, "hash", new List<Guid>());
            db.Staff.Add(first);
            await db.SaveChangesAsync();
            db.Staff.Remove(first);
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDb())
        {
            var second = E.Staff.Create($"E2{Guid.NewGuid():N}"[..10], "Second", "Holder", email, "hash", new List<Guid>());
            db.Staff.Add(second);
            var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
            Assert.Null(ex);   // the soft-deleted holder no longer reserves the email
        }
    }
}
