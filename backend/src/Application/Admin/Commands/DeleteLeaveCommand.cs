using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Commands;

public record DeleteLeaveCommand(Guid Id) : IRequest;

public class DeleteLeaveCommandHandler(IAppDbContext db) : IRequestHandler<DeleteLeaveCommand>
{
    public async Task Handle(DeleteLeaveCommand req, CancellationToken ct)
    {
        var leave = await db.LeaveRequests.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Leave request {req.Id} not found.");
        db.LeaveRequests.Remove(leave);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // xmin token mismatch: another session modified this leave row between our
            // FindAsync and this DELETE. Surface a friendly error so the admin can refresh.
            throw new InvalidOperationException(
                "This leave request was modified concurrently. Please refresh and try again.");
        }
    }
}
