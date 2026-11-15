using HRNexus.Domain.Common;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Who reports to whom decides who may sign off whose pay. WHY these tests matter: a loop in the reporting
/// line (A reports to B, B reports to A) would let two people sign off each other and leave nobody at the top,
/// and a person who reports to themselves would sign their own pay.
/// </summary>
public class ReportingLineTests
{
    private static readonly Guid Ceo = Guid.NewGuid();
    private static readonly Guid Hod = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Worker = Guid.NewGuid();

    /// <summary>Worker -> Manager -> Hod -> Ceo -> nobody.</summary>
    private static readonly IReadOnlyDictionary<Guid, Guid?> Chain = new Dictionary<Guid, Guid?>
    {
        [Worker] = Manager, [Manager] = Hod, [Hod] = Ceo, [Ceo] = null,
    };

    [Fact]
    public void Moving_someone_under_a_peer_is_fine() =>
        Assert.False(ReportingLine.WouldLoop(Worker, Hod, Chain));

    [Fact]
    public void Nobody_reports_to_themselves() =>
        Assert.True(ReportingLine.WouldLoop(Manager, Manager, Chain));

    [Fact]
    public void Making_the_boss_report_to_someone_below_them_would_loop() =>
        Assert.True(ReportingLine.WouldLoop(Ceo, Worker, Chain));

    [Fact]
    public void A_loop_through_several_levels_is_found() =>
        Assert.True(ReportingLine.WouldLoop(Hod, Manager, Chain));

    [Fact]
    public void A_chain_that_already_loops_does_not_hang_the_check()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var broken = new Dictionary<Guid, Guid?> { [a] = b, [b] = a };

        Assert.True(ReportingLine.WouldLoop(Guid.NewGuid(), a, broken));
    }

    [Fact]
    public void A_person_can_have_no_manager() =>
        Assert.False(ReportingLine.WouldLoop(Ceo, null, Chain));

    [Fact]
    public void Staff_report_to_one_person_and_can_be_cleared()
    {
        var s = E.Staff.Create("EMP1", "Eve", "Tan", null, "hash", []);

        s.SetReportsTo(Manager);
        Assert.Equal(Manager, s.ReportsToId);

        s.SetReportsTo(null);
        Assert.Null(s.ReportsToId);
    }

    [Fact]
    public void Staff_cannot_report_to_themselves()
    {
        var s = E.Staff.Create("EMP1", "Eve", "Tan", null, "hash", []);

        Assert.Throws<ArgumentException>(() => s.SetReportsTo(s.Id));
    }
}
