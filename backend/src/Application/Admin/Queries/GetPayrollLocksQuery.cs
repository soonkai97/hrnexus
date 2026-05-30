using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

/// <summary>Lists locked payroll periods, newest first. Optionally filtered to a single year.</summary>
public record GetPayrollLocksQuery(int? Year = null) : IRequest<List<PayrollLockDto>>;

public record PayrollLockDto(int Year, int Month, DateTime LockedAt, string LockedBy, string? Note);

public class GetPayrollLocksQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPayrollLocksQuery, List<PayrollLockDto>>
{
    public async Task<List<PayrollLockDto>> Handle(GetPayrollLocksQuery req, CancellationToken ct)
    {
        var query = db.PayrollLocks.AsNoTracking();
        if (req.Year is { } year)
            query = query.Where(p => p.Year == year);

        return await query
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PayrollLockDto(p.Year, p.Month, p.LockedAt, p.LockedBy, p.Note))
            .ToListAsync(ct);
    }
}
