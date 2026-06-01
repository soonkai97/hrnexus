using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Staff.Commands;

/// <summary>One row of a staff import. Password is optional — when omitted a strong temporary
/// password is generated and returned so the admin can hand it to the new staff member.</summary>
public record BulkStaffRow(
    string StaffId, string FirstName, string? LastName, string? Email,
    List<string> Roles, List<string>? Locations = null, string? Password = null);

/// <summary>Per-row outcome. On success, <see cref="TempPassword"/> carries the generated
/// password (only returned here, never stored in plaintext).</summary>
public record BulkStaffRowResult(string StaffId, bool Success, string? Error, string? TempPassword);

public record BulkStaffImportResult(int Created, int Failed, List<BulkStaffRowResult> Rows);

/// <summary>Register many staff at once with PARTIAL SUCCESS — a bad row is reported and skipped,
/// the rest still import. Validates each row (required fields, duplicate Staff ID / email within
/// the batch and against the DB) before adding; all valid rows commit in one SaveChanges.</summary>
public record BulkRegisterStaffCommand(List<BulkStaffRow> Rows) : IRequest<BulkStaffImportResult>;

public class BulkRegisterStaffCommandHandler(IAppDbContext db, IPasswordHasher hasher, IAuditService audit)
    : IRequestHandler<BulkRegisterStaffCommand, BulkStaffImportResult>
{
    public async Task<BulkStaffImportResult> Handle(BulkRegisterStaffCommand req, CancellationToken ct)
    {
        if (req.Rows.Count == 0) return new BulkStaffImportResult(0, 0, []);
        if (req.Rows.Count > 100)
            throw new ArgumentException("Cannot import more than 100 staff at once.");

        // Pre-load existing identifiers once so each row's duplicate check is in-memory.
        var existingIds    = (await db.Staff.Select(s => s.StaffId).ToListAsync(ct)).ToHashSet();
        var existingEmails = (await db.Staff.Where(s => s.Email != null).Select(s => s.Email!).ToListAsync(ct)).ToHashSet();

        var seenIds    = new HashSet<string>();
        var seenEmails = new HashSet<string>();
        var results    = new List<BulkStaffRowResult>();
        var toAdd       = new List<(E.Staff Staff, string Id, string Temp)>();

        foreach (var row in req.Rows)
        {
            var id = (row.StaffId ?? "").Trim().ToUpperInvariant();
            if (id.Length == 0)          { results.Add(new("(blank)", false, "Staff ID is required.", null)); continue; }
            if (id.Length > 20)          { results.Add(new(id, false, "Staff ID must be 20 characters or fewer.", null)); continue; }
            if (string.IsNullOrWhiteSpace(row.FirstName)) { results.Add(new(id, false, "First name is required.", null)); continue; }
            if (row.Roles is null || row.Roles.Count == 0) { results.Add(new(id, false, "At least one role is required.", null)); continue; }
            if (existingIds.Contains(id) || !seenIds.Add(id)) { results.Add(new(id, false, "Staff ID already in use.", null)); continue; }

            var email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim().ToLowerInvariant();
            if (email != null && (existingEmails.Contains(email) || !seenEmails.Add(email)))
            {
                results.Add(new(id, false, "Email already in use.", null)); continue;
            }

            var temp = string.IsNullOrWhiteSpace(row.Password) ? GeneratePassword() : row.Password!;
            try { PasswordPolicy.Validate(temp); }
            catch (Exception ex) { results.Add(new(id, false, ex.Message, null)); continue; }

            var staff = E.Staff.Create(id, row.FirstName, row.LastName, email, hasher.Hash(temp),
                row.Roles, locations: row.Locations);
            toAdd.Add((staff, id, temp));
            results.Add(new(id, true, null, temp));
        }

        if (toAdd.Count > 0)
        {
            foreach (var (staff, id, _) in toAdd)
            {
                db.Staff.Add(staff);
                await audit.LogAsync("Staff", staff.Id.ToString(), "BulkCreate", null, "Admin", $"StaffId={id}", ct);
            }
            await db.SaveChangesAsync(ct);
        }

        return new BulkStaffImportResult(toAdd.Count, results.Count - toAdd.Count, results);
    }

    /// <summary>Generates a policy-compliant temporary password (≥8 chars, ≥1 upper, ≥1 digit).</summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digit = "23456789";
        const string all   = upper + lower + digit;
        var rng   = Random.Shared;
        var chars = new List<char> { upper[rng.Next(upper.Length)], digit[rng.Next(digit.Length)] };
        for (var i = 0; i < 8; i++) chars.Add(all[rng.Next(all.Length)]);
        for (var i = chars.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (chars[i], chars[j]) = (chars[j], chars[i]); }
        return new string([.. chars]);
    }
}
