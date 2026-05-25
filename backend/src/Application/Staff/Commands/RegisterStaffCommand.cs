using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Staff.Commands;

public record RegisterStaffCommand(
    string        StaffId,
    string        FirstName,
    string        LastName,
    string        Email,
    string        Password,
    List<string>  Roles,
    string?       PhoneNumber   = null,
    string?       IcNo          = null,
    string?       PassportNo    = null,
    DateTime?     DateOfBirth   = null) : IRequest<Guid>;

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
        if (string.IsNullOrWhiteSpace(req.LastName))
            throw new InvalidOperationException("Last name is required.");
        if (string.IsNullOrWhiteSpace(req.Email))
            throw new InvalidOperationException("Email is required.");
        if (string.IsNullOrWhiteSpace(req.IcNo) && string.IsNullOrWhiteSpace(req.PassportNo))
            throw new InvalidOperationException("IC number or Passport number is required.");
        if (!req.DateOfBirth.HasValue)
            throw new InvalidOperationException("Date of birth is required.");

        var idTaken = await db.Staff.AnyAsync(e => e.StaffId == normalizedId, ct);
        if (idTaken)
            throw new InvalidOperationException($"Staff ID '{normalizedId}' is already in use.");

        var emailTaken = await db.Staff.AnyAsync(e => e.Email == req.Email.ToLowerInvariant(), ct);
        if (emailTaken)
            throw new InvalidOperationException($"Email '{req.Email}' is already registered.");

        var employee = HRNexus.Domain.Entities.Staff.Create(normalizedId, req.FirstName, req.LastName, req.Email,
            hasher.Hash(req.Password), req.Roles,
            PhoneNormalizer.Normalize(req.PhoneNumber),
            req.IcNo, req.PassportNo, req.DateOfBirth);

        employee.SetMustChangePassword();
        db.Staff.Add(employee);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Staff", employee.Id.ToString(), "Created",
            null, "Admin", $"StaffId={normalizedId}", ct);
        return employee.Id;
    }
}
