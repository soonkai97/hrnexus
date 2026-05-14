using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.FeatureFlags.Commands;

public record UpdateFeatureFlagsCommand(Dictionary<string, bool> Flags) : IRequest;

public class UpdateFeatureFlagsHandler(IAppDbContext db)
    : IRequestHandler<UpdateFeatureFlagsCommand>
{
    public async Task Handle(UpdateFeatureFlagsCommand request, CancellationToken ct)
    {
        var existing = await db.FeatureFlags.ToListAsync(ct);

        foreach (var (key, value) in request.Flags)
        {
            var flag = existing.FirstOrDefault(f => f.Key == key);
            if (flag is null)
            {
                db.FeatureFlags.Add(FeatureFlag.Create(key, value));
            }
            else
            {
                flag.SetEnabled(value);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
