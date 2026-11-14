using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRNexus.Domain.Payroll;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The hand-worked payroll examples in tests/fixtures/payroll-calculator.json, one test per example.
///
/// WHY these tests matter: every expected figure in the fixture was worked out by hand (the formula is in
/// each example's "working"), NOT produced by this calculator, so the calculator is checked against an
/// independent calculation. They prove "the calculator follows the rate rows it is given", never "the rows
/// match the law": that second question is the accountant's (docs/process/payroll-accountant-questions.md).
/// The accountant review pack is rendered from the same file.
/// </summary>
public class PayrollCalculatorVectorTests
{
    // ── fixture loading ───────────────────────────────────────────────────────────

    private static JsonElement Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tests", "fixtures", "payroll-calculator.json")))
            dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "payroll-calculator.json"))).RootElement;
    }

    public static IEnumerable<object[]> VectorNames()
    {
        foreach (var v in Fixture().GetProperty("vectors").EnumerateArray())
            yield return [v.GetProperty("name").GetString()!];
    }

    [Fact]
    public void The_fixture_is_not_empty() =>
        Assert.True(Fixture().GetProperty("vectors").GetArrayLength() >= 15);

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void The_calculator_reproduces_the_hand_worked_example(string name)
    {
        var fixture = Fixture();
        var vector = fixture.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("name").GetString() == name);
        var input = BuildInput(fixture.GetProperty("rates"), vector.GetProperty("input"));

        var result = PayrollCalculator.Calculate(input);

        var e = vector.GetProperty("expected");
        Assert.Equal(M(e, "proratedBasic"), result.ProratedBasic);
        Assert.Equal(M(e, "unpaidLeave"), result.UnpaidLeaveDeduction);
        Assert.Equal(M(e, "gross"), result.Gross);
        Assert.Equal(M(e, "net"), result.Net);
        Assert.Equal(M(e, "employerCost"), result.EmployerCost);
        Assert.Equal(M(e, "unapplied"), result.UnappliedDeductions);
        Assert.Equal(e.GetProperty("employedDays").GetInt32(), result.Basis.EmployedDays);

        foreach (var line in e.GetProperty("lines").EnumerateObject())
        {
            var actual = result.Lines.Where(l => l.Type == line.Name).Sum(l => l.Amount);
            Assert.True(line.Value.GetDecimal() == actual, $"line '{line.Name}': expected {line.Value.GetDecimal()} but the calculator gave {actual}");
        }

        if (e.TryGetProperty("deductionsApplied", out var applied))
            Assert.Equal(applied.EnumerateArray().Select(x => x.GetDecimal()),
                result.Lines.Where(l => l.Type == "deduction").Select(l => l.Amount));

        Assert.Equal(e.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()!).OrderBy(x => x),
            result.Warnings.Select(w => w.Code).OrderBy(x => x));
    }

    // ── building the calculator input from a fixture example ──────────────────────

    private static decimal M(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    private static Guid IdFor(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes("rate:" + key)));

    private static DateOnly D(string s) => DateOnly.Parse(s, CultureInfo.InvariantCulture);

    internal static PayrollInput BuildInput(JsonElement rates, JsonElement i)
    {
        var year = i.GetProperty("year").GetInt32();
        var month = i.GetProperty("month").GetInt32();
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var hrdActive = !i.TryGetProperty("hrdActive", out var ha) || ha.GetBoolean();

        var segments = i.TryGetProperty("segments", out var segs)
            ? segs.EnumerateArray().Select(s => new SalarySegment(D(s.GetProperty("from").GetString()!), D(s.GetProperty("to").GetString()!), s.GetProperty("basic").GetDecimal())).ToList()
            : [new SalarySegment(first, last, i.GetProperty("basic").GetDecimal())];

        var unpaid = i.TryGetProperty("unpaidLeave", out var ul)
            ? ul.EnumerateArray().Select(u => new UnpaidLeaveDay(D(u.GetProperty("date").GetString()!), u.GetProperty("days").GetDecimal())).ToList()
            : [];

        var earnings = i.TryGetProperty("earnings", out var er)
            ? er.EnumerateArray().Select(x => new EarningInput(
                x.GetProperty("type").GetString()!, x.GetProperty("reason").GetString()!, x.GetProperty("amount").GetDecimal(),
                x.GetProperty("countsForEpf").GetBoolean(), x.GetProperty("countsForSocsoEis").GetBoolean())).ToList()
            : [];

        var deductions = i.TryGetProperty("deductions", out var dd)
            ? dd.EnumerateArray().Select(x => new DeductionInput(x.GetProperty("reason").GetString()!, x.GetProperty("amount").GetDecimal(), x.GetProperty("priority").GetInt32())).ToList()
            : [];

        decimal? pcb = 0m;
        if (i.TryGetProperty("pcb", out var p)) pcb = p.ValueKind == JsonValueKind.Null ? null : p.GetDecimal();

        string? klass = "citizen";
        if (i.TryGetProperty("class", out var c)) klass = c.ValueKind == JsonValueKind.Null ? null : c.GetString();

        return new PayrollInput(
            year, month,
            AgeAtMonthEnd: i.TryGetProperty("age", out var a) ? a.GetInt32() : 30,
            CitizenshipClass: klass,
            EmploymentStart: i.TryGetProperty("employmentStart", out var es) ? D(es.GetString()!) : null,
            EmploymentEnd: i.TryGetProperty("employmentEnd", out var ee) ? D(ee.GetString()!) : null,
            Segments: segments,
            UnpaidLeave: unpaid,
            Divisor: i.TryGetProperty("divisor", out var dv) ? dv.GetDecimal() : 26m,
            Earnings: earnings,
            Deductions: deductions,
            DeductionCapRate: i.TryGetProperty("deductionCapRate", out var cap) ? cap.GetDecimal() : 0.5m,
            PcbAmount: pcb,
            Rates: rates.EnumerateArray().Select(r => BuildRate(r, hrdActive)).ToList());
    }

    private static RateRule BuildRate(JsonElement r, bool hrdActive)
    {
        var key = r.GetProperty("key").GetString()!;
        decimal? Opt(string n) => r.TryGetProperty(n, out var x) && x.ValueKind != JsonValueKind.Null ? x.GetDecimal() : null;
        return new RateRule(
            IdFor(key),
            r.GetProperty("scheme").GetString()!,
            r.GetProperty("class").GetString()!,
            AgeFrom: 0, AgeTo: null,
            EffectiveFrom: new DateOnly(2020, 1, 1),
            Kind: r.GetProperty("kind").GetString() == "band-table" ? RateKind.BandTable : RateKind.Percentage,
            WageCeiling: Opt("wageCeiling"),
            EmployeeRate: Opt("employeeRate") ?? 0m,
            EmployerRate: Opt("employerRate") ?? 0m,
            EmployerRateMode: r.GetProperty("employerRateMode").GetString() == "whole-wage-by-threshold" ? EmployerRateMode.WholeWageByThreshold : EmployerRateMode.Flat,
            EmployerRateThreshold: Opt("employerRateThreshold"),
            EmployerRateAbove: Opt("employerRateAbove"),
            Rounding: r.GetProperty("rounding").GetString() switch
            {
                "none" => RoundingRule.None,
                "nearest-cent" => RoundingRule.NearestCent,
                "nearest-5-sen" => RoundingRule.Nearest5Sen,
                "up-to-next-ringgit" => RoundingRule.UpToNextRinggit,
                var other => throw new InvalidOperationException($"unknown rounding '{other}'"),
            },
            Amount: Opt("amount"),
            Active: r.GetProperty("scheme").GetString() == "hrd-compulsory" ? hrdActive : true,
            Verified: true,
            Bands: []);
    }
}
