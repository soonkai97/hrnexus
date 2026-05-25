using MediatR;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

public record UpsertOvertimeRuleCommand(
    Guid?   Id,
    string  Role,
    decimal ThresholdHours,
    decimal Multiplier,
    bool    IsActive = true) : IRequest<Guid>;

public class UpsertOvertimeRuleCommandHandler(IAppDbContext db)
    : IRequestHandler<UpsertOvertimeRuleCommand, Guid>
{
    public async Task<Guid> Handle(UpsertOvertimeRuleCommand req, CancellationToken ct)
    {
        if (req.ThresholdHours <= 0)
            throw new ArgumentException("ThresholdHours must be greater than zero.");
        if (req.Multiplier < 1.0m)
            throw new ArgumentException("Multiplier must be at least 1.0 (pay cannot be reduced by overtime rules).");

        if (req.Id.HasValue)
        {
            var rule = await db.OvertimeRules.FindAsync([req.Id.Value], ct)
                ?? throw new KeyNotFoundException($"Overtime rule {req.Id} not found.");
            rule.Update(req.Role, req.ThresholdHours, req.Multiplier, req.IsActive);
            await db.SaveChangesAsync(ct);
            return rule.Id;
        }
        else
        {
            var rule = OvertimeRule.Create(req.Role, req.ThresholdHours, req.Multiplier);
            if (!req.IsActive)
                rule.Update(rule.Role, rule.ThresholdHours, rule.Multiplier, false);
            db.OvertimeRules.Add(rule);
            await db.SaveChangesAsync(ct);
            return rule.Id;
        }
    }
}
