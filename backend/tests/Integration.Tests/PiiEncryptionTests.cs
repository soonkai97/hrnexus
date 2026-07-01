using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Proves PII is encrypted AT REST against real Postgres: the raw column bytes are opaque ciphertext
/// (the "enc1:" marker), while reading the same row back through EF transparently decrypts to the
/// original plaintext. This is the guarantee L4 exists to provide.
/// </summary>
[Collection("postgres")]
public class PiiEncryptionTests(PostgresFixture fx)
{
    [Fact]
    public async Task StaffPii_IsCiphertextInTheColumn_ButPlaintextThroughEf()
    {
        var dob = new DateTime(1990, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        Guid id;

        await using (var db = fx.NewDb())
        {
            // Roles aren't relevant to PII encryption — seed none (avoids an FK to a non-existent role).
            var staff = E.Staff.Create("PIITEST1", "Pii", "Tester", "piitest1@t.com",
                "hash", new List<Guid>(),
                phoneNumber: "0123456789", icNo: "900615-10-5523", passportNo: "A12345678", dateOfBirth: dob);
            db.Staff.Add(staff);
            await db.SaveChangesAsync();
            id = staff.Id;
        }

        // Raw column values must be encrypted (start with the marker, not the plaintext).
        await using (var db = fx.NewDb())
        {
            var rawIc = await db.Database
                .SqlQueryRaw<string>(@"SELECT ""IcNo"" AS ""Value"" FROM ""Staff"" WHERE ""Id"" = {0}", id)
                .SingleAsync();
            var rawPhone = await db.Database
                .SqlQueryRaw<string>(@"SELECT ""PhoneNumber"" AS ""Value"" FROM ""Staff"" WHERE ""Id"" = {0}", id)
                .SingleAsync();
            var rawDob = await db.Database
                .SqlQueryRaw<string>(@"SELECT ""DateOfBirth"" AS ""Value"" FROM ""Staff"" WHERE ""Id"" = {0}", id)
                .SingleAsync();

            Assert.StartsWith("enc1:", rawIc);
            Assert.DoesNotContain("900615", rawIc);     // plaintext IC not present in the column
            Assert.StartsWith("enc1:", rawPhone);
            Assert.StartsWith("enc1:", rawDob);
        }

        // Reading back through EF transparently decrypts.
        await using (var db = fx.NewDb())
        {
            var staff = await db.Staff.SingleAsync(s => s.Id == id);
            Assert.Equal("900615-10-5523", staff.IcNo);
            Assert.Equal("A12345678", staff.PassportNo);
            Assert.Equal("0123456789", staff.PhoneNumber);
            Assert.Equal(dob, staff.DateOfBirth);
        }
    }
}
