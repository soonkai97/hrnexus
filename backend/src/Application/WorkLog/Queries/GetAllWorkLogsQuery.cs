using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.WorkLog.Queries;

public record GetAllWorkLogsQuery(
    DateTime? From,
    DateTime? To,
    Guid?     EmployeeId,
    int       Page     = 1,
    int       PageSize = 10) : IRequest<PagedResult<WorkLogDto>>;

public class GetAllWorkLogsQueryHandler(IAppDbContext db, IDateTimeProvider dt) : IRequestHandler<GetAllWorkLogsQuery, PagedResult<WorkLogDto>>
{
    public async Task<PagedResult<WorkLogDto>> Handle(GetAllWorkLogsQuery req, CancellationToken ct)
    {
        var query = db.WorkLogs.Include(w => w.Employee).AsQueryable();

        if (req.EmployeeId.HasValue)
            query = query.Where(w => w.EmployeeId == req.EmployeeId.Value);

        if (req.From.HasValue)
            query = query.Where(w => w.WorkDate >= DateOnly.FromDateTime(req.From.Value));

        if (req.To.HasValue)
            query = query.Where(w => w.WorkDate <= DateOnly.FromDateTime(req.To.Value));

        var ordered = query.OrderByDescending(w => w.WorkDate).ThenByDescending(w => w.StartTime);

        var total = await ordered.CountAsync(ct);
        var logs  = await ordered
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var items = logs.Select(w =>
        {
            var isActive = w.EndTime == null;
            var endHhMm  = w.EndTime ?? $"{dt.LocalNow.Hour:D2}:{dt.LocalNow.Minute:D2}";
            var hours    = CalcHours(w.StartTime, endHhMm);
            return new WorkLogDto(w.Id, w.RoleName, w.LocationName, w.WorkDate.ToString("yyyy-MM-dd"),
                w.StartTime, w.EndTime, Math.Round(hours, 2),
                Math.Round(hours * w.HourlyRateSnapshot, 2), isActive, w.IsManualEntry, w.Notes);
        }).ToList();

        return new PagedResult<WorkLogDto>(items, total);
    }

    private static decimal CalcHours(string start, string end)
    {
        var s = TimeSpan.Parse(start);
        var e = TimeSpan.Parse(end);
        var diff = e > s ? e - s : e + TimeSpan.FromHours(24) - s;
        return (decimal)diff.TotalHours;
    }
}
