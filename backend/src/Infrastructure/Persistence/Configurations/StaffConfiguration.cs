using System.Globalization;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> b)
    {
        b.ToTable("Staff");
        b.HasKey(e => e.Id);
        b.Property(e => e.StaffId).HasMaxLength(20).IsRequired();
        // Partial unique index (excludes soft-deleted rows) so a Staff ID can be reused after the
        // previous holder is deleted — uniqueness applies only among ACTIVE staff, matching the
        // global !IsDeleted query filter. A non-partial index would leave a deleted row occupying
        // the ID and reject the re-add (surfacing as a misleading "email already registered").
        b.HasIndex(e => e.StaffId).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(e => e.FirstName).HasMaxLength(50).IsRequired();
        b.Property(e => e.LastName).HasMaxLength(50).IsRequired();
        // Email is optional contact info only — it is NOT used to log in or recover a password (Staff ID
        // is), so it carries no unique constraint: staff may share one address or have none.
        b.Property(e => e.Email).HasMaxLength(200);
        b.Property(e => e.PasswordHash).IsRequired();

        // ── PII encrypted at rest (AES-GCM via value converters) ─────────────────
        // IcNo/PassportNo/PhoneNumber become opaque ciphertext text columns (the old length caps are
        // dropped — ciphertext is longer). DateOfBirth is serialized round-trip and encrypted to text.
        // None of these are queried/indexed, so randomised encryption is safe here.
        // The encryptor passes null straight through, so a nullable converter keeps NULLs as NULL.
        var stringCipher = new ValueConverter<string?, string?>(
            v => encryptor.Encrypt(v),
            v => encryptor.Decrypt(v));
        b.Property(e => e.IcNo).HasConversion(stringCipher);
        b.Property(e => e.PassportNo).HasConversion(stringCipher);
        b.Property(e => e.PhoneNumber).HasConversion(stringCipher);

        var dobCipher = new ValueConverter<DateTime, string>(
            v => encryptor.Encrypt(v.ToString("O", CultureInfo.InvariantCulture))!,
            v => DateTime.Parse(encryptor.Decrypt(v)!, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        b.Property(e => e.DateOfBirth).HasConversion(dobCipher).HasColumnType("text");

        // Roles are normalised into a child EmployeeRoles table (one-to-many).
        // AutoInclude means every LINQ query loads roles without an explicit Include().
        b.HasMany(e => e.RoleEntries)
            .WithOne()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(e => e.RoleEntries).AutoInclude();

        // RefreshToken as a concurrency token: EF includes it in the UPDATE WHERE clause.
        // If two concurrent 401s both read the same token, the second SaveChanges finds
        // 0 rows (token already rotated) and throws DbUpdateConcurrencyException.
        b.Property(e => e.RefreshToken).IsConcurrencyToken();

        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);
        b.Property(e => e.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.DeletedBy).HasMaxLength(100);

        b.HasQueryFilter(e => !e.IsDeleted);

        // Computed properties — not mapped to columns.
        b.Ignore(e => e.Roles);
        b.Ignore(e => e.RoleIds);
        b.Ignore(e => e.FullName);
        b.Ignore(e => e.Age);
        b.Ignore(e => e.PermissionLevel);
    }
}
