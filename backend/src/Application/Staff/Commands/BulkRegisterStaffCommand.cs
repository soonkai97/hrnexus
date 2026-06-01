using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Staff.Commands;

/// <summary>One row of a staff import. The admin supplies each member's password (it must meet the
/// password policy); the staff member logs in with it and may change it later.</summary>
public record BulkStaffRow(
    string StaffId, string FirstName, string? LastName, string? Email,
    List<string> Roles, List<string>? Locations = null, string? Password = null);

/// <summary>Per-row outcome — success, or the validation error that skipped the row.</summary>
public record BulkStaffRowResult(string StaffId, bool Success, string? Error);

public record BulkStaffImportResult(int Created, int Failed, List<BulkStaffRowResult> Rows);

/// <summary>Register many staff at once with PARTIAL SUCCESS — a bad row is reported and skipped,
/// the rest still import. Validates each row (required fields, password policy, duplicate Staff ID /
/// email within the batch and against the DB) before adding; all valid rows commit in one SaveChanges.</summary>
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
        var toAdd       = new List<(E.Staff Staff, string Id)>();

        foreach (var row in req.Rows)
        {
            var id = (row.StaffId ?? "").Trim().ToUpperInvariant();
            if (id.Length == 0)          { results.Add(new("(blank)", false, "Staff ID is required.")); continue; }
            if (id.Length > 20)          { results.Add(new(id, false, "Staff ID must be 20 characters or fewer.")); continue; }
            if (string.IsNullOrWhiteSpace(row.FirstName)) { results.Add(new(id, false, "First name is required.")); continue; }
            if (row.Roles is null || row.Roles.Count == 0) { results.Add(new(id, false, "At least one role is required.")); continue; }
            if (existingIds.Contains(id) || !seenIds.Add(id)) { results.Add(new(id, false, "Staff ID already in use.")); continue; }

            var email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim().ToLowerInvariant();
            if (email != null && (existingEmails.Contains(email) || !seenEmails.Add(email)))
            {
                results.Add(new(id, false, "Email already in use.")); continue;
            }

            var pwd = (row.Password ?? "").Trim();
            if (pwd.Length == 0) { results.Add(new(id, false, "Password is required.")); continue; }
            try { PasswordPolicy.Validate(pwd); }
            catch (Exception ex) { results.Add(new(id, false, ex.Message)); continue; }

            var staff = E.Staff.Create(id, row.FirstName, row.LastName, email, hasher.Hash(pwd),
                row.Roles, locations: row.Locations);
            toAdd.Add((staff, id));
            results.Add(new(id, true, null));
        }

        if (toAdd.Count > 0)
        {
            foreach (var (staff, id) in toAdd)
            {
                db.Staff.Add(staff);
                await audit.LogAsync("Staff", staff.Id.ToString(), "BulkCreate", null, "Admin", $"StaffId={id}", ct);
            }
            await db.SaveChangesAsync(ct);
        }

        return new BulkStaffImportResult(toAdd.Count, results.Count - toAdd.Count, results);
    }
}
