using MediatR;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Commands;

public record DeleteLeaveCommand(Guid Id) : IRequest;

public class DeleteLeaveCommandHandler(IAppDbContext db) : IRequestHandler<DeleteLeaveCommand>
{
    public async Task Handle(DeleteLeaveCommand req, CancellationToken ct)
    {
        var leave = await db.LeaveRequests.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Leave request {req.Id} not found.");
        db.LeaveRequests.Remove(leave);
        await db.SaveChangesAsync(ct);
    }
}
