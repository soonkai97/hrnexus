using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Queries;

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
        var query = db.WorkLogs.AsQueryable();

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

        var loc    = await RoleLocalization.BuildAsync(db, logs.Select(l => l.RoleName), ct);
        var locLoc = await LocationLocalization.BuildByIdsAsync(db, logs.Select(l => l.LocationId), ct);
        var items = logs.Select(w =>
        {
            var isActive = w.EndTime == null;
            // Active shift → measure to "now"; closed shift → use its recorded end date+time
            // (multi-day aware). Fall back to deriving the end date for any legacy un-backfilled row.
            var endDate = isActive
                ? DateOnly.FromDateTime(dt.LocalNow)
                : (w.EndDate ?? ShiftHours.DeriveEndDate(w.WorkDate, w.StartTime, w.EndTime!));
            var endTime = isActive ? $"{dt.LocalNow.Hour:D2}:{dt.LocalNow.Minute:D2}" : w.EndTime!;
            var hours   = ShiftHours.CalcHours(w.WorkDate, w.StartTime, endDate, endTime);
            var (zh, vi) = loc.For(w.RoleName);
            var (locZh, locVi) = locLoc.For(w.LocationId);
            return new WorkLogDto(w.Id, w.RoleName, w.LocationId, w.LocationName, w.WorkDate.ToString("yyyy-MM-dd"),
                w.StartTime, w.EndDate?.ToString("yyyy-MM-dd"), w.EndTime, Math.Round(hours, 2),
                Math.Round(ShiftPay.Calc(w.PayTypeSnapshot, hours, w.HourlyRateSnapshot), 2), isActive, w.IsManualEntry, w.IsEdited, w.Notes, zh, vi, locZh, locVi);
        }).ToList();

        return new PagedResult<WorkLogDto>(items, total);
    }
}
