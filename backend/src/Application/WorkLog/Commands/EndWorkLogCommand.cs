using MediatR;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;

namespace HRNexus.Application.WorkLog.Commands;

public record EndWorkLogCommand(Guid Id, Guid EmployeeId, string EndTime) : IRequest;

public class EndWorkLogCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub)
    : IRequestHandler<EndWorkLogCommand>
{
    public async Task Handle(EndWorkLogCommand req, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");
        if (log.EmployeeId != req.EmployeeId) throw new UnauthorizedAccessException("Not your shift.");
        if (log.EndTime != null) throw new InvalidOperationException("Shift already ended.");

        // Validate end time produces a non-zero duration (guard against identical start/end)
        var hours = HRNexus.Application.Common.OvertimeCalculator.CalcHours(log.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from the shift start time.");

        log.End(req.EndTime);
        await db.SaveChangesAsync(ct);

        // Broadcast updated live stats to all connected dashboard clients
        var stats = await sender.Send(new GetLiveStatsQuery(), ct);
        await hub.BroadcastLiveStatsAsync(stats, ct);
    }
}
