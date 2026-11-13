using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Queries;

public record SalaryProfileDto(Guid Id, DateOnly EffectiveFrom, decimal BasicSalary, string PayFrequency,
    string? BankName, string? BankAccount, string? BankHolder, DateTime CreatedAt);

public record EmploymentDto(string? CitizenshipStatus, DateOnly? StartDate, DateOnly? EndDate);

/// <param name="Current">The profile in effect today (latest row whose effective date has arrived), or null.</param>
/// <param name="History">Every row, newest effective date first, including future-dated ones.</param>
/// <param name="Employment">Citizenship status and employment dates (what payroll needs to prorate).</param>
public record SalaryDto(SalaryProfileDto? Current, List<SalaryProfileDto> History, EmploymentDto Employment);

/// <summary>A person's pay history and employment details. Callers must hold payroll.view (enforced at the endpoint).</summary>
public record GetSalaryQuery(Guid EmployeeId) : IRequest<SalaryDto>;

public class GetSalaryQueryHandler(IAppDbContext db, IDateTimeProvider dt) : IRequestHandler<GetSalaryQuery, SalaryDto>
{
    public async Task<SalaryDto> Handle(GetSalaryQuery req, CancellationToken ct)
    {
        var employment = await db.Staff.Where(s => s.Id == req.EmployeeId)
            .Select(s => new EmploymentDto(s.CitizenshipStatus, s.EmploymentStartDate, s.EmploymentEndDate))
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Staff {req.EmployeeId} not found.");

        var rows = await db.SalaryProfiles.Where(p => p.EmployeeId == req.EmployeeId)
            .OrderByDescending(p => p.EffectiveFrom)
            .Select(p => new SalaryProfileDto(p.Id, p.EffectiveFrom, p.BasicSalary, p.PayFrequency,
                p.BankName, p.BankAccount, p.BankHolder, p.CreatedAt))
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(dt.LocalNow);
        return new SalaryDto(rows.FirstOrDefault(r => r.EffectiveFrom <= today), rows, employment);
    }
}
