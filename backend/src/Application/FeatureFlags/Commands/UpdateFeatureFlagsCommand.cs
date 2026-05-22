using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Constants;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.FeatureFlags.Commands;

public record UpdateFeatureFlagsCommand(Dictionary<string, bool> Flags) : IRequest;

public class UpdateFeatureFlagsHandler(IAppDbContext db)
    : IRequestHandler<UpdateFeatureFlagsCommand>
{
    /// <summary>All valid feature flag keys. Reject any unknown key to prevent DB pollution.</summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        AppConstants.FeatureFlags.Tips,
        AppConstants.FeatureFlags.TipsHistory,
        AppConstants.FeatureFlags.Payroll,
        AppConstants.FeatureFlags.PaySummary,
        AppConstants.FeatureFlags.Announcements,
        AppConstants.FeatureFlags.StaffLedger,
    };

    public async Task Handle(UpdateFeatureFlagsCommand request, CancellationToken ct)
    {
        var unknownKeys = request.Flags.Keys
            .Where(k => !KnownKeys.Contains(k))
            .ToList();

        if (unknownKeys.Count > 0)
            throw new ArgumentException(
                $"Unknown feature flag key(s): {string.Join(", ", unknownKeys)}. " +
                $"Valid keys are: {string.Join(", ", KnownKeys)}.");

        var existing = await db.FeatureFlags.ToListAsync(ct);

        foreach (var (key, value) in request.Flags)
        {
            var flag = existing.FirstOrDefault(f => f.Key == key);
            if (flag is null)
                db.FeatureFlags.Add(FeatureFlag.Create(key, value));
            else
                flag.SetEnabled(value);
        }

        await db.SaveChangesAsync(ct);
    }
}
