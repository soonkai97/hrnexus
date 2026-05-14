using MediatR;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Commands;

public record DeleteOvertimeRuleCommand(Guid Id) : IRequest;

public class DeleteOvertimeRuleCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteOvertimeRuleCommand>
{
    public async Task Handle(DeleteOvertimeRuleCommand req, CancellationToken ct)
    {
        var rule = await db.OvertimeRules.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Overtime rule {req.Id} not found.");
        db.OvertimeRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }
}
