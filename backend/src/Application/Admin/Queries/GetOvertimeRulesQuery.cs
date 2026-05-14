using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

public record GetOvertimeRulesQuery : IRequest<List<OvertimeRuleDto>>;

public record OvertimeRuleDto(
    Guid    Id,
    string  Role,
    decimal ThresholdHours,
    decimal Multiplier,
    bool    IsActive);

public class GetOvertimeRulesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetOvertimeRulesQuery, List<OvertimeRuleDto>>
{
    public async Task<List<OvertimeRuleDto>> Handle(GetOvertimeRulesQuery _, CancellationToken ct)
    {
        return await db.OvertimeRules
            .OrderBy(r => r.ThresholdHours)
            .Select(r => new OvertimeRuleDto(r.Id, r.Role, r.ThresholdHours, r.Multiplier, r.IsActive))
            .ToListAsync(ct);
    }
}
