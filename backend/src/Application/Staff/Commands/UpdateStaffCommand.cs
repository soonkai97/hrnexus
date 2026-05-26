using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Commands;

public record UpdateStaffCommand(
    Guid          Id,
    string        StaffId,
    string        FirstName,
    string?       LastName,
    string?       Email,
    List<string>  Roles,
    string?       PhoneNumber,
    string?       IcNo          = null,
    string?       PassportNo    = null,
    DateTime?     DateOfBirth   = null) : IRequest;

public class UpdateStaffCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<UpdateStaffCommand>
{
    public async Task Handle(UpdateStaffCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FirstName)) throw new ArgumentException("First name is required.");

        // FirstOrDefaultAsync (not FindAsync) so that AutoInclude on RoleEntries is honoured.
        var emp = await db.Staff.FirstOrDefaultAsync(e => e.Id == req.Id, ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");

        var normalizedId = req.StaffId.Trim().ToUpperInvariant();

        var idTaken = await db.Staff.AnyAsync(
            e => e.StaffId == normalizedId && e.Id != req.Id, ct);
        if (idTaken)
            throw new InvalidOperationException($"Staff ID '{normalizedId}' is already in use.");

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var emailTaken = await db.Staff.AnyAsync(
                e => e.Email == req.Email.ToLowerInvariant() && e.Id != req.Id, ct);
            if (emailTaken)
                throw new InvalidOperationException($"Email '{req.Email}' is already in use.");
        }

        // Resolve email: keep existing if not supplied
        var resolvedEmail = string.IsNullOrWhiteSpace(req.Email) ? emp.Email : req.Email;

        emp.UpdateDetails(normalizedId, req.FirstName, req.LastName, resolvedEmail, req.Roles,
            PhoneNormalizer.Normalize(req.PhoneNumber),
            req.IcNo, req.PassportNo, req.DateOfBirth);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Staff", req.Id.ToString(), "Updated",
            null, "Admin", $"StaffId={normalizedId}", ct);
    }
}
