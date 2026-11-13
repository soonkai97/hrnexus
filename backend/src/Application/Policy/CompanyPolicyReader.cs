using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Policy;

/// <summary>
/// Reads the company policy. The table holds at most one tiny row, so each leave operation simply reads it
/// (no cache to invalidate); when nothing is saved yet the defaults apply.
/// </summary>
public static class CompanyPolicyReader
{
    public static async Task<CompanyPolicy> GetAsync(IAppDbContext db, CancellationToken ct) =>
        await db.CompanyPolicies.AsNoTracking().FirstOrDefaultAsync(ct) ?? CompanyPolicy.Default();
}
