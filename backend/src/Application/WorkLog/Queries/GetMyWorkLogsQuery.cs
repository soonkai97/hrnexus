using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Queries;

public record GetMyWorkLogsQuery(Guid EmployeeId, int Page = 1, int PageSize = 10) : IRequest<PagedResult<WorkLogDto>>;

// RoleNameZh/RoleNameVi are the job's CURRENT localized display names, resolved by matching the
// frozen RoleName snapshot back to the Roles table (both are UPPERCASE). They're null when the job
// was since deleted or renamed — the client then falls back to the RoleName snapshot. RoleName itself
// stays the record-of-truth for what the shift was logged under.
public record WorkLogDto(Guid Id, string RoleName, string LocationId, string LocationName, string WorkDate, string StartTime, string? EndDate, string? EndTime, decimal Hours, decimal Cost, bool IsActive, bool IsManualEntry, bool IsEdited, string? Notes, string? RoleNameZh = null, string? RoleNameVi = null);

public class GetMyWorkLogsQueryHandler(IAppDbContext db) : IRequestHandler<GetMyWorkLogsQuery, PagedResult<WorkLogDto>>
{
    public async Task<PagedResult<WorkLogDto>> Handle(GetMyWorkLogsQuery req, CancellationToken ct)
    {
        var query = db.WorkLogs
            .Where(w => w.EmployeeId == req.EmployeeId)
            .OrderByDescending(w => w.WorkDate)
            .ThenByDescending(w => w.StartTime);

        var total = await query.CountAsync(ct);
        var logs  = await query
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var loc = await RoleLocalization.BuildAsync(db, logs.Select(l => l.RoleName), ct);
        return new PagedResult<WorkLogDto>(logs.Select(w => ToDto(w, loc)).ToList(), total);
    }

    private static WorkLogDto ToDto(HRNexus.Domain.Entities.WorkLog w, RoleLocalization loc)
    {
        var isActive = w.EndTime == null || w.EndDate == null;
        var hours    = isActive ? 0m : ShiftHours.CalcHours(w.WorkDate, w.StartTime, w.EndDate!.Value, w.EndTime!);
        var (zh, vi) = loc.For(w.RoleName);
        return new WorkLogDto(w.Id, w.RoleName, w.LocationId, w.LocationName, w.WorkDate.ToString("yyyy-MM-dd"), w.StartTime, w.EndDate?.ToString("yyyy-MM-dd"), w.EndTime, Math.Round(hours, 2), Math.Round(ShiftPay.Calc(w.PayTypeSnapshot, hours, w.HourlyRateSnapshot), 2), isActive, w.IsManualEntry, w.IsEdited, w.Notes, zh, vi);
    }
}
