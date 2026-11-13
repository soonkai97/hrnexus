using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Policy;

// ExpectedVersion is the version the editor loaded (0 or null when nothing was saved yet); a save from a stale copy is refused.
public record UpdateCompanyPolicyCommand(
    int[]   WorkingDays,
    decimal WorkdayHours,
    int     AnnualLeaveAdvanceDays,
    int     MaxLeaveDays,
    string  CarryOverMode,
    decimal CarryOverMaxHours,
    decimal UnpaidLeaveDivisor,
    int?    ExpectedVersion) : IRequest<int>;

public class UpdateCompanyPolicyCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<UpdateCompanyPolicyCommand, int>
{
    /// <returns>The new version.</returns>
    public async Task<int> Handle(UpdateCompanyPolicyCommand req, CancellationToken ct)
    {
        var existing = await db.CompanyPolicies.FirstOrDefaultAsync(ct);
        var current = existing?.Version ?? 0;
        if (req.ExpectedVersion is { } expected && expected != current)
            throw new InvalidOperationException("The company policy was changed by someone else. Reload it and try again.");

        var policy = existing ?? CompanyPolicy.Default();
        policy.Update(req.WorkingDays, req.WorkdayHours, req.AnnualLeaveAdvanceDays, req.MaxLeaveDays,
            req.CarryOverMode, req.CarryOverMaxHours, req.UnpaidLeaveDivisor, updatedBy: null);
        if (existing is null) db.CompanyPolicies.Add(policy);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two first saves raced and the second insert hit the single-row key.
            throw new InvalidOperationException("The company policy was changed by someone else. Reload it and try again.");
        }

        await audit.LogAsync("CompanyPolicy", CompanyPolicy.SingletonId.ToString(), "Updated", null, "Admin",
            $"Working days {string.Join(",", req.WorkingDays)}; {req.WorkdayHours:0.##}h/day; carry-over {policy.CarryOverMode}", ct);
        return policy.Version;
    }
}
