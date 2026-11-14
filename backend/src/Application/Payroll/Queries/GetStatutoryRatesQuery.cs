using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Payroll;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Queries;

public record StatutoryRateBandDto(decimal WageFrom, decimal WageTo, decimal EmployeeAmount, decimal EmployerAmount);

/// <summary>A rate row for display. Rates are not secret; salary figures never appear here.</summary>
public record StatutoryRateDto(
    Guid Id, string Scheme, string CitizenshipClass, int AgeFrom, int? AgeTo, DateOnly EffectiveFrom, string Kind,
    decimal? WageCeiling, decimal EmployeeRate, decimal EmployerRate, string EmployerRateMode,
    decimal? EmployerRateThreshold, decimal? EmployerRateAbove, string Rounding, decimal? Amount, bool Active,
    bool Verified, string? VerifiedBy, DateTime? VerifiedAt, string? VerificationNote, string Source, string? Note,
    IReadOnlyList<StatutoryRateBandDto> Bands);

public record GetStatutoryRatesQuery : IRequest<List<StatutoryRateDto>>;

public class GetStatutoryRatesQueryHandler(IAppDbContext db) : IRequestHandler<GetStatutoryRatesQuery, List<StatutoryRateDto>>
{
    public async Task<List<StatutoryRateDto>> Handle(GetStatutoryRatesQuery req, CancellationToken ct)
    {
        var rows = await db.StatutoryRateTables.ToListAsync(ct);
        var order = StatutoryScheme.All.ToList();
        return rows
            .OrderBy(t => order.IndexOf(t.Scheme)).ThenBy(t => t.CitizenshipClass).ThenByDescending(t => t.EffectiveFrom)
            .Select(t => new StatutoryRateDto(
                t.Id, t.Scheme, t.CitizenshipClass, t.AgeFrom, t.AgeTo, t.EffectiveFrom,
                t.Kind == RateKind.BandTable ? "band-table" : "percentage",
                t.WageCeiling, t.EmployeeRate, t.EmployerRate,
                t.EmployerRateMode == EmployerRateMode.WholeWageByThreshold ? "whole-wage-by-threshold" : "flat",
                t.EmployerRateThreshold, t.EmployerRateAbove,
                t.Rounding switch
                {
                    RoundingRule.NearestCent => "nearest-cent",
                    RoundingRule.Nearest5Sen => "nearest-5-sen",
                    RoundingRule.UpToNextRinggit => "up-to-next-ringgit",
                    _ => "none",
                },
                t.Amount, t.Active, t.Verified, t.VerifiedBy, t.VerifiedAt, t.VerificationNote, t.Source, t.Note,
                t.ToRule().Bands.Select(b => new StatutoryRateBandDto(b.WageFrom, b.WageTo, b.EmployeeAmount, b.EmployerAmount)).ToList()))
            .ToList();
    }
}
