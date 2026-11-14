using HRNexus.Domain.Entities;
using HRNexus.Domain.Leave;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The rules a leave type must obey (Phase 7q) and the catalogue an untouched installation starts with.
///
/// WHY these tests matter: a leave type is company data that decides who is charged what and who may
/// approve what. Each rule below exists to stop a configuration that would rewrite history (changing the
/// pool of a type that already has leave), let a long absence approve itself (auto-approve without a
/// balance), or lock the company out of a system behaviour (no-show, sick-pattern warnings).
/// </summary>
public class LeaveTypeDefinitionTests
{
    private static LeaveTypeSpec Spec(string key = "study", string pool = "none", decimal? allowance = null,
        bool autoApprove = false, bool adminOnly = false, int? notice = null, bool fromPolicy = false,
        decimal? maxDays = null, string colour = "info", string? label = null) =>
        new(key, label ?? "Study", colour, 10, true, pool, allowance, maxDays, autoApprove, notice, fromPolicy,
            DeductsPay: false, AdminOnly: adminOnly, CountsCalendarDays: false, Sensitive: false);

    // ── creation rules ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Study")]            // upper case
    [InlineData("study leave")]      // space
    [InlineData("-study")]           // leading hyphen
    [InlineData("study-")]           // trailing hyphen
    [InlineData("")]
    [InlineData("a-key-that-is-much-too-long")]   // more than 20 characters: the column is 20
    public void A_key_is_a_short_lower_case_slug(string key) =>
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(key)));

    [Fact]
    public void A_valid_key_is_kept_as_given()
    {
        var t = LeaveTypeDefinition.Create(Spec("study-leave"));
        Assert.Equal("study-leave", t.Key);
        Assert.False(t.IsSystem);
    }

    [Fact]
    public void An_own_pool_needs_an_allowance_and_other_pools_must_not_have_one()
    {
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "own")));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "own", allowance: 0m)));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "none", allowance: 5m)));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "annual", allowance: 5m)));
        Assert.Equal(60m, LeaveTypeDefinition.Create(Spec(pool: "own", allowance: 60m)).AllowanceDays);
    }

    [Fact]
    public void Auto_approve_needs_a_balance_because_it_skips_the_own_decision_check()
    {
        // A 98-day absence must never approve itself: only types limited by a balance may auto-approve.
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "none", autoApprove: true)));
        Assert.True(LeaveTypeDefinition.Create(Spec(pool: "sick", autoApprove: true)).AutoApprove);
    }

    [Fact]
    public void An_admin_only_type_cannot_auto_approve() =>
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "sick", autoApprove: true, adminOnly: true)));

    [Fact]
    public void The_pool_and_the_colour_come_from_fixed_lists()
    {
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(pool: "shared")));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(colour: "purple")));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(colour: "")));
    }

    [Fact]
    public void Day_limits_and_notice_have_sane_bounds()
    {
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(maxDays: 0.4m)));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(maxDays: 367m)));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(notice: -1)));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(notice: 61)));
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(notice: 5, fromPolicy: true)));   // one source of truth
        Assert.Equal(98m, LeaveTypeDefinition.Create(Spec(maxDays: 98m)).MaxDaysPerRequest);
    }

    [Fact]
    public void A_label_is_required_and_trimmed()
    {
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.Create(Spec(label: "  ")));
        Assert.Equal("Study", LeaveTypeDefinition.Create(Spec(label: "  Study ")).Label);
    }

    // ── editing rules ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_key_never_changes()
    {
        var t = LeaveTypeDefinition.Create(Spec("study"));
        Assert.Throws<ArgumentException>(() => t.Update(Spec("exam"), hasLeave: false, "admin"));
    }

    [Fact]
    public void The_pool_and_the_day_counting_are_locked_once_the_type_has_leave()
    {
        var t = LeaveTypeDefinition.Create(Spec("study", pool: "none"));

        // Changing them would refund or recharge hours already booked.
        Assert.Throws<InvalidOperationException>(() => t.Update(Spec("study", pool: "own", allowance: 10m), hasLeave: true, "admin"));
        Assert.Throws<InvalidOperationException>(() => t.Update(Spec("study") with { CountsCalendarDays = true }, hasLeave: true, "admin"));

        // Everything else stays editable, and nothing is locked while there is no leave.
        t.Update(Spec("study", label: "Exam leave", maxDays: 5m), hasLeave: true, "admin");
        Assert.Equal("Exam leave", t.Label);
        t.Update(Spec("study", pool: "own", allowance: 10m), hasLeave: false, "admin");
        Assert.Equal("own", t.Pool);
    }

    [Fact]
    public void A_system_type_keeps_its_behaviour_but_may_be_renamed_and_recoloured()
    {
        var sick = LeaveTypeDefaults.All().Single(t => t.Key == LeaveTypeDefaults.SickKey);
        Assert.True(sick.IsSystem);

        Assert.Throws<InvalidOperationException>(() => sick.Update(ToSpec(sick) with { Pool = "none", AutoApprove = false }, false, "admin"));
        Assert.Throws<InvalidOperationException>(() => sick.Update(ToSpec(sick) with { Active = false }, false, "admin"));

        sick.Update(ToSpec(sick) with { Label = "Medical", Colour = "success", Sensitive = true }, false, "admin");
        Assert.Equal("Medical", sick.Label);
        Assert.True(sick.Sensitive);
    }

    private static LeaveTypeSpec ToSpec(LeaveTypeDefinition t) => new(t.Key, t.Label, t.Colour, t.SortOrder, t.Active, t.Pool,
        t.AllowanceDays, t.MaxDaysPerRequest, t.AutoApprove, t.AdvanceNoticeDays, t.AdvanceNoticeFromPolicy,
        t.DeductsPay, t.AdminOnly, t.CountsCalendarDays, t.Sensitive);

    // ── the catalogue as a whole ─────────────────────────────────────────────────

    [Fact]
    public void A_catalogue_needs_a_type_people_can_apply_for_and_no_repeated_keys()
    {
        var all = LeaveTypeDefaults.All().ToList();
        LeaveTypeDefinition.EnsureValidCatalogue(all);   // the defaults are valid

        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.EnsureValidCatalogue([.. all, LeaveTypeDefinition.Create(Spec("annual"))]));

        foreach (var t in all.Where(t => !t.IsSystem)) t.Update(ToSpec(t) with { Active = false }, false, "a");
        // Only the two system types remain active and no-show is admin-only, so sick is the one choice left: still valid.
        LeaveTypeDefinition.EnsureValidCatalogue(all);
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.EnsureValidCatalogue([]));
    }

    [Fact]
    public void A_catalogue_must_keep_both_system_types()
    {
        var withoutSick = LeaveTypeDefaults.All().Where(t => t.Key != LeaveTypeDefaults.SickKey).ToList();
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.EnsureValidCatalogue(withoutSick));
        var withoutNoShow = LeaveTypeDefaults.All().Where(t => t.Key != LeaveTypeDefaults.NoShowKey).ToList();
        Assert.Throws<ArgumentException>(() => LeaveTypeDefinition.EnsureValidCatalogue(withoutNoShow));
    }

    // ── the defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_defaults_hold_the_owners_six_types_plus_the_three_that_already_existed()
    {
        var keys = LeaveTypeDefaults.All().Select(t => t.Key).ToList();
        Assert.Equal(
            ["annual", "emergency", "sick", "hospitalisation", "maternity", "paternity", "compassionate", "unpaid", "no-show"],
            keys);
    }

    [Fact]
    public void The_five_original_types_behave_as_they_did_before_the_catalogue_existed()
    {
        var d = LeaveTypeDefaults.All().ToDictionary(t => t.Key);

        // annual: its own per-person pool, needs notice taken from the company policy
        Assert.Equal("annual", d["annual"].Pool);
        Assert.True(d["annual"].AdvanceNoticeFromPolicy);
        Assert.False(d["annual"].AutoApprove);
        // emergency draws on the annual pool and approves itself
        Assert.Equal("annual", d["emergency"].Pool);
        Assert.True(d["emergency"].AutoApprove);
        // sick: its own pool, approves itself, a locked system type
        Assert.Equal("sick", d["sick"].Pool);
        Assert.True(d["sick"].AutoApprove);
        Assert.True(d["sick"].IsSystem);
        // unpaid and no-show: no balance, pay is deducted; no-show is admin-only and a system type
        Assert.Equal("none", d["unpaid"].Pool);
        Assert.True(d["unpaid"].DeductsPay);
        Assert.True(d["no-show"].AdminOnly);
        Assert.True(d["no-show"].IsSystem);
        Assert.True(d["no-show"].DeductsPay);
        // none of the original five hides its name in messages (so today's wording is unchanged)
        Assert.All(new[] { "annual", "emergency", "sick", "unpaid", "no-show" }, k => Assert.False(d[k].Sensitive));
    }

    [Fact]
    public void The_new_types_follow_the_assumed_statutory_figures_and_are_sensitive()
    {
        var d = LeaveTypeDefaults.All().ToDictionary(t => t.Key);

        Assert.Equal("own", d["hospitalisation"].Pool);
        Assert.Equal(60m, d["hospitalisation"].AllowanceDays);

        Assert.Equal("none", d["maternity"].Pool);
        Assert.Equal(98m, d["maternity"].MaxDaysPerRequest);
        Assert.True(d["maternity"].CountsCalendarDays);

        Assert.Equal(7m, d["paternity"].MaxDaysPerRequest);
        Assert.True(d["paternity"].CountsCalendarDays);

        Assert.Equal(3m, d["compassionate"].MaxDaysPerRequest);
        Assert.False(d["compassionate"].CountsCalendarDays);

        Assert.All(new[] { "hospitalisation", "maternity", "paternity", "compassionate" }, k =>
        {
            Assert.True(d[k].Sensitive);
            Assert.False(d[k].AutoApprove);   // a long absence never approves itself
            Assert.True(d[k].Active);
        });
    }

    [Fact]
    public void Every_default_is_a_valid_type_and_each_call_returns_fresh_instances()
    {
        var a = LeaveTypeDefaults.All();
        var b = LeaveTypeDefaults.All();
        Assert.NotSame(a[0], b[0]);   // entities are mutable: callers must never share them
        LeaveTypeDefinition.EnsureValidCatalogue(a);
    }
}
