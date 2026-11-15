using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Queries;

/// <summary>A recurring allowance or deduction. InUse means a submitted or locked run has used it: it can be ended but no longer deleted.</summary>
public record PayItemDto(Guid Id, string Kind, string Reason, decimal Amount, bool CountsForEpf, bool CountsForSocsoEis, int Priority,
    DateOnly From, DateOnly? To, DateTime CreatedAt, bool InUse);

/// <summary>A person's recurring allowances and deductions, newest first. The amounts are pay: the caller must hold payroll.view (checked here as well as at the endpoint).</summary>
public record GetPayItemsQuery(Guid EmployeeId, Guid By) : IRequest<List<PayItemDto>>;

public class GetPayItemsQueryHandler(IAppDbContext db) : IRequestHandler<GetPayItemsQuery, List<PayItemDto>>
{
    public async Task<List<PayItemDto>> Handle(GetPayItemsQuery req, CancellationToken ct)
    {
        await PayrollAccess.RequireAsync(db, AppConstants.Permissions.PayrollView, req.By, ct);
        if (!await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, ct))
            throw new KeyNotFoundException($"Staff {req.EmployeeId} not found.");

        var items = await db.PayItems.AsNoTracking().Where(i => i.EmployeeId == req.EmployeeId).ToListAsync(ct);
        var used = await PayItemUse.UsedByFrozenRunsAsync(db, items.Select(i => i.Id).ToList(), ct);
        return items.OrderByDescending(i => i.From).ThenBy(i => i.Reason)
            .Select(i => new PayItemDto(i.Id, i.Kind, i.Reason, i.Amount, i.CountsForEpf, i.CountsForSocsoEis, i.Priority, i.From, i.To, i.CreatedAt, used.Contains(i.Id)))
            .ToList();
    }
}
