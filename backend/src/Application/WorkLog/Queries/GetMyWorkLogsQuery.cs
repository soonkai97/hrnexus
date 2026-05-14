using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.WorkLog.Queries;

public record GetMyWorkLogsQuery(Guid EmployeeId, int Page = 1, int PageSize = 10) : IRequest<PagedResult<WorkLogDto>>;

public record WorkLogDto(Guid Id, string RoleName, string LocationName, string WorkDate, string StartTime, string? EndTime, decimal Hours, decimal Cost, bool IsActive, bool IsManualEntry, string? Notes);

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

    private static WorkLogDto ToDto(NightclubWorkforce.Domain.Entities.WorkLog w)
    {
        var isActive = w.EndTime == null;
        var hours    = isActive ? 0m : CalcHours(w.StartTime, w.EndTime!);
        return new WorkLogDto(w.Id, w.RoleName, w.LocationName, w.WorkDate.ToString("yyyy-MM-dd"), w.StartTime, w.EndTime, Math.Round(hours, 2), Math.Round(hours * w.HourlyRateSnapshot, 2), isActive, w.IsManualEntry, w.Notes);
    }

    private static decimal CalcHours(string start, string end)
    {
        var s = TimeSpan.Parse(start);
        var e = TimeSpan.Parse(end);
        var diff = e > s ? e - s : e + TimeSpan.FromHours(24) - s; // handle overnight
        return (decimal)diff.TotalHours;
    }
}
