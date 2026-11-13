using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands;

/// <summary>Sets what payroll needs from the staff record: statutory class (citizenship) and employment dates (proration).</summary>
public record SetEmploymentDetailsCommand(Guid EmployeeId, string CitizenshipStatus, DateOnly StartDate, DateOnly? EndDate) : IRequest;

public class SetEmploymentDetailsCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<SetEmploymentDetailsCommand>
{
    public async Task Handle(SetEmploymentDetailsCommand req, CancellationToken ct)
    {
        var status = req.CitizenshipStatus?.Trim().ToLowerInvariant() ?? "";
        if (status is not (AppConstants.Payroll.Citizen or AppConstants.Payroll.PermanentResident))
            throw new ArgumentException("Citizenship status must be 'citizen' or 'permanent-resident'.");
        if (req.EndDate is { } end && end < req.StartDate)
            throw new ArgumentException("The employment end date cannot be before the start date.");

        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Staff {req.EmployeeId} not found.");

        staff.SetEmployment(status, req.StartDate, req.EndDate);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Staff", staff.Id.ToString(), "EmploymentUpdated", null, "Admin",
            $"Employment {req.StartDate:yyyy-MM-dd} to {(req.EndDate?.ToString("yyyy-MM-dd") ?? "open")}", ct);
    }
}
