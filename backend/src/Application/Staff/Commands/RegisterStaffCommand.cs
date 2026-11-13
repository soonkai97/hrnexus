using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Commands;

public record RegisterStaffCommand(
    string        StaffId,
    string        FirstName,
    string?       LastName,
    string?       Email,
    string        Password,
    List<Guid>    Roles,
    string?       PhoneNumber   = null,
    string?       IcNo          = null,
    string?       PassportNo    = null,
    DateTime?     DateOfBirth   = null,
    List<string>? Locations     = null) : IRequest<Guid>;

public class RegisterStaffCommandHandler(IAppDbContext db, IPasswordHasher hasher, IAuditService audit)
    : IRequestHandler<RegisterStaffCommand, Guid>
{
    public async Task<Guid> Handle(RegisterStaffCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.StaffId))
            throw new InvalidOperationException("Staff ID is required.");

        var normalizedId = req.StaffId.Trim().ToUpperInvariant();

        if (normalizedId.Length > 20)
            throw new InvalidOperationException("Staff ID must be 20 characters or fewer.");
        if (string.IsNullOrWhiteSpace(req.FirstName))
            throw new InvalidOperationException("First name is required.");

        PasswordPolicy.Validate(req.Password);

        var idTaken = await db.Staff.AnyAsync(e => e.StaffId == normalizedId, ct);
        if (idTaken)
            throw new InvalidOperationException($"Staff ID '{normalizedId}' is already in use.");

        var employee = HRNexus.Domain.Entities.Staff.Create(normalizedId, req.FirstName, req.LastName, req.Email,
            hasher.Hash(req.Password), req.Roles,
            PhoneNormalizer.Normalize(req.PhoneNumber),
            req.IcNo, req.PassportNo, req.DateOfBirth, req.Locations);

        db.Staff.Add(employee);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The only unique constraint on insert is the (partial) StaffId index — a concurrent
            // request registered the same Staff ID between our check and our save. (Email has no
            // unique constraint.)
            throw new InvalidOperationException($"Staff ID '{normalizedId}' is already in use.");
        }
        await audit.LogAsync("Staff", employee.Id.ToString(), "Created",
            null, "Admin", $"StaffId={normalizedId}", ct);
        return employee.Id;
    }
}
