using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Queries;

public record GetMyWorkLogsQuery(Guid EmployeeId, int Page = 1, int PageSize = 10) : IRequest<PagedResult<WorkLogDto>>;

public record WorkLogDto(Guid Id, string RoleName, string LocationId, string LocationName, string WorkDate, string StartTime, string? EndDate, string? EndTime, decimal Hours, decimal Cost, bool IsActive, bool IsManualEntry, bool IsEdited, string? Notes);

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

        return new PagedResult<WorkLogDto>(logs.Select(ToDto).ToList(), total);
    }

    private static WorkLogDto ToDto(HRNexus.Domain.Entities.WorkLog w)
    {
        var isActive = w.EndTime == null || w.EndDate == null;
        var hours    = isActive ? 0m : ShiftHours.CalcHours(w.WorkDate, w.StartTime, w.EndDate!.Value, w.EndTime!);
        return new WorkLogDto(w.Id, w.RoleName, w.LocationId, w.LocationName, w.WorkDate.ToString("yyyy-MM-dd"), w.StartTime, w.EndDate?.ToString("yyyy-MM-dd"), w.EndTime, Math.Round(hours, 2), Math.Round(ShiftPay.Calc(w.PayTypeSnapshot, hours, w.HourlyRateSnapshot), 2), isActive, w.IsManualEntry, w.IsEdited, w.Notes);
    }
}
