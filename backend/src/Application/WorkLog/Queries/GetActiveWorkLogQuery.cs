using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Queries;

public record GetActiveWorkLogQuery(Guid EmployeeId) : IRequest<WorkLogDto?>;

public class GetActiveWorkLogQueryHandler(IAppDbContext db) : IRequestHandler<GetActiveWorkLogQuery, WorkLogDto?>
{
    public async Task<WorkLogDto?> Handle(GetActiveWorkLogQuery req, CancellationToken ct)
    {
        var w = await db.WorkLogs.FirstOrDefaultAsync(w => w.EmployeeId == req.EmployeeId && w.EndTime == null, ct);
        if (w == null) return null;
        return new WorkLogDto(w.Id, w.RoleName, w.LocationId, w.LocationName, w.WorkDate.ToString("yyyy-MM-dd"), w.StartTime, w.EndDate?.ToString("yyyy-MM-dd"), w.EndTime, 0, 0, true, w.IsManualEntry, w.IsEdited, w.Notes);
    }
}
