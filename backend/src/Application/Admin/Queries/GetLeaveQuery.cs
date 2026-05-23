using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

public record GetLeaveQuery(
    Guid?     EmployeeId,
    DateTime? DateFrom,
    DateTime? DateTo,
    string?   Status    = null,
    string?   LeaveType = null,
    int       Page      = 1,
    int       PageSize  = 10) : IRequest<PagedResult<LeaveDto>>;

public record LeaveDto(
    Guid     Id,
    Guid     EmployeeId,
    string   StaffId,
    string   StaffName,
    string   LeaveType,
    string   StartDate,
    string   EndDate,
    string?  StartTime,
    string?  EndTime,
    decimal  Hours,
    string?  Notes,
    string   Status,
    string   CreatedAt);

public class GetLeaveQueryHandler(IAppDbContext db)
    : IRequestHandler<GetLeaveQuery, PagedResult<LeaveDto>>
{
    public async Task<PagedResult<LeaveDto>> Handle(GetLeaveQuery req, CancellationToken ct)
    {
        var query = db.LeaveRequests.Include(l => l.Employee).AsQueryable();

        if (req.EmployeeId.HasValue)
            query = query.Where(l => l.EmployeeId == req.EmployeeId.Value);

        if (req.DateFrom.HasValue)
            query = query.Where(l => l.EndDate >= req.DateFrom.Value.AsUtcDate());

        if (req.DateTo.HasValue)
            query = query.Where(l => l.StartDate <= req.DateTo.Value.AsUtcDate());

        if (!string.IsNullOrEmpty(req.Status))
            query = query.Where(l => l.Status == req.Status);

        if (!string.IsNullOrEmpty(req.LeaveType))
            query = query.Where(l => l.LeaveType == req.LeaveType.ToLowerInvariant());

        // Pending first; within each group oldest submission first (first applied = first reviewed)
        query = query
            .OrderByDescending(l => l.Status == "pending")
            .ThenBy(l => l.CreatedAt);

        var total = await query.CountAsync(ct);
        var page  = Math.Max(1, req.Page);
        var size  = Math.Clamp(req.PageSize, 1, 200);
        var list  = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var items = list.Select(l => new LeaveDto(
            l.Id,
            l.EmployeeId,
            l.Employee?.StaffId  ?? "",
            l.Employee?.FullName ?? "",
            l.LeaveType,
            l.StartDate.ToString("yyyy-MM-dd"),
            l.EndDate.ToString("yyyy-MM-dd"),
            l.StartTime,
            l.EndTime,
            l.Hours ?? 0m,
            l.Notes,
            l.Status,
            l.CreatedAt.ToString("yyyy-MM-dd HH:mm"))).ToList();

        return new PagedResult<LeaveDto>(items, total);
    }
}
