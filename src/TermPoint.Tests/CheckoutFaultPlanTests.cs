using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="CheckoutFaultPlan"/>, the DEBUG-ONLY fault injection behind the
/// <c>TERMPOINT_DEBUG_FAIL_CHECKOUT</c> environment variable (spec item 4d). The parser and the
/// budget accounting are pure and run in every configuration. Only the environment read is
/// configuration-dependent: it is compiled out of Release builds, so the one test that touches the
/// variable asserts the matching behaviour for whichever configuration it was built in.
///
/// <para>How a plan makes <see cref="CheckoutService"/> fail is covered in
/// <see cref="CheckoutFailedOpenTests"/>.</para>
/// </summary>
public sealed class CheckoutFaultPlanTests
{
    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Each documented value yields a plan for the matching fault, case-insensitively and ignoring
    /// surrounding whitespace, and without a count it is unlimited.
    /// </summary>
    [Theory]
    [InlineData("unreachable", InjectedCheckoutFault.Unreachable)]
    [InlineData("missing",     InjectedCheckoutFault.Missing)]
    [InlineData("unreadable",  InjectedCheckoutFault.Unreadable)]
    [InlineData("snapshot",    InjectedCheckoutFault.Snapshot)]
    [InlineData("UNREACHABLE", InjectedCheckoutFault.Unreachable)]
    [InlineData("  Missing  ", InjectedCheckoutFault.Missing)]
    public void Parse_KnownValue_ReturnsUnlimitedPlan(string spec, InjectedCheckoutFault expected)
    {
        var plan = CheckoutFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Null(plan.Remaining);
    }

    /// <summary>A <c>value:count</c> form limits the plan to that many calls.</summary>
    [Theory]
    [InlineData("unreachable:1", InjectedCheckoutFault.Unreachable, 1)]
    [InlineData("snapshot:3",    InjectedCheckoutFault.Snapshot,    3)]
    [InlineData(" Missing : 2 ", InjectedCheckoutFault.Missing,     2)]
    [InlineData("unreadable:10", InjectedCheckoutFault.Unreadable,  10)]
    public void Parse_ValueWithCount_ReturnsCountedPlan(string spec, InjectedCheckoutFault expected, int count)
    {
        var plan = CheckoutFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Equal(count, plan.Remaining);
    }

    /// <summary>
    /// The <c>value:count:skip</c> form lets the first <c>skip</c> matching calls through (the app's own
    /// startup open is the first checkout of the process); <c>*</c> as the count means "every call".
    /// </summary>
    [Theory]
    [InlineData("unreachable:1:1",   InjectedCheckoutFault.Unreachable, 1,    1)]
    [InlineData("unreachable:*:1",   InjectedCheckoutFault.Unreachable, null, 1)]
    [InlineData("snapshot:2:0",      InjectedCheckoutFault.Snapshot,    2,    0)]
    [InlineData(" Missing : * : 3 ", InjectedCheckoutFault.Missing,     null, 3)]
    [InlineData("unreadable:*",      InjectedCheckoutFault.Unreadable,  null, 0)]
    public void Parse_ValueWithCountAndSkip_ReturnsPlanThatSkipsFirst(
        string spec, InjectedCheckoutFault expected, int? count, int skip)
    {
        var plan = CheckoutFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Equal(count, plan.Remaining);
        Assert.Equal(skip, plan.Skip);
    }

    /// <summary>
    /// Garbage is ignored — no plan, no exception — so a typo in a launch profile can never break a
    /// session: unknown names, empty text, extra parts, and a count or skip that is missing, zero (for
    /// the count), negative, signed, fractional or not a number.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    [InlineData("1")]
    [InlineData("none")]
    [InlineData(":2")]
    [InlineData("unreachable:")]
    [InlineData("unreachable:0")]
    [InlineData("unreachable:-1")]
    [InlineData("unreachable:+1")]
    [InlineData("unreachable:1.5")]
    [InlineData("unreachable:abc")]
    [InlineData("unreachable:1:2:3")]
    [InlineData("unreachable:1:")]
    [InlineData("unreachable:1:-1")]
    [InlineData("unreachable:1:abc")]
    [InlineData("unreachable::1")]
    [InlineData("unreachable:**")]
    [InlineData("unreachable:*:x")]
    [InlineData("unreachable:99999999999")]
    [InlineData("unreachable,snapshot")]
    public void Parse_Garbage_ReturnsNull(string? spec)
    {
        Assert.Null(CheckoutFaultPlan.Parse(spec));
    }

    // ── Budget accounting ─────────────────────────────────────────────────────

    /// <summary>
    /// An unlimited checkout fault fires on every checkout, for as long as the plan exists — "Try again"
    /// keeps failing until the variable is cleared and the app restarted.
    /// </summary>
    [Fact]
    public void TryConsumeCheckoutFault_Unlimited_FiresEveryTime()
    {
        var plan = CheckoutFaultPlan.Parse("missing")!;

        for (var i = 0; i < 25; i++)
        {
            Assert.True(plan.TryConsumeCheckoutFault(out var fault));
            Assert.Equal(InjectedCheckoutFault.Missing, fault);
        }

        Assert.Null(plan.Remaining);
    }

    /// <summary>A counted plan fails exactly that many checkouts, then lets every later one through.</summary>
    [Fact]
    public void TryConsumeCheckoutFault_Counted_FiresExactlyNTimes()
    {
        var plan = CheckoutFaultPlan.Parse("unreachable:2")!;

        Assert.True(plan.TryConsumeCheckoutFault(out _));
        Assert.Equal(1, plan.Remaining);
        Assert.True(plan.TryConsumeCheckoutFault(out _));
        Assert.Equal(0, plan.Remaining);

        Assert.False(plan.TryConsumeCheckoutFault(out var fault));
        Assert.Equal(InjectedCheckoutFault.None, fault);
        Assert.False(plan.TryConsumeCheckoutFault(out _));
        Assert.Equal(0, plan.Remaining);
    }

    /// <summary>
    /// <c>unreachable:1:1</c>: the first matching call (startup) is let through, the second fails, every
    /// later one is let through again — "fail one switch, then let the retry from the chooser succeed".
    /// </summary>
    [Fact]
    public void TryConsumeCheckoutFault_CountedWithSkip_LetsTheFirstThrough_ThenFailsExactlyN()
    {
        var plan = CheckoutFaultPlan.Parse("unreachable:1:1")!;

        Assert.False(plan.TryConsumeCheckoutFault(out _));     // startup
        Assert.Equal(0, plan.Skip);
        Assert.Equal(1, plan.Remaining);                        // the budget is untouched by a skipped call

        Assert.True(plan.TryConsumeCheckoutFault(out var fault)); // the switch
        Assert.Equal(InjectedCheckoutFault.Unreachable, fault);

        Assert.False(plan.TryConsumeCheckoutFault(out _));     // the retry from the chooser
        Assert.False(plan.TryConsumeCheckoutFault(out _));
    }

    /// <summary><c>unreachable:*:1</c>: startup is let through, then EVERY later checkout fails.</summary>
    [Fact]
    public void TryConsumeCheckoutFault_UnlimitedWithSkip_LetsTheFirstThrough_ThenAlwaysFails()
    {
        var plan = CheckoutFaultPlan.Parse("unreachable:*:1")!;

        Assert.False(plan.TryConsumeCheckoutFault(out _));
        for (var i = 0; i < 10; i++)
            Assert.True(plan.TryConsumeCheckoutFault(out _));
    }

    /// <summary>
    /// The snapshot fault belongs to the read-only snapshot only: it never fails a checkout, and the
    /// three checkout faults never fail a snapshot — and a call that is not the plan's own does not use
    /// up its budget.
    /// </summary>
    [Fact]
    public void Faults_OnlyAnswerTheirOwnCall_AndDoNotSpendTheBudgetOnOthers()
    {
        var snapshotPlan = CheckoutFaultPlan.Parse("snapshot:1")!;
        Assert.False(snapshotPlan.TryConsumeCheckoutFault(out _));
        Assert.Equal(1, snapshotPlan.Remaining);
        Assert.True(snapshotPlan.TryConsumeSnapshotFault());
        Assert.False(snapshotPlan.TryConsumeSnapshotFault());

        var checkoutPlan = CheckoutFaultPlan.Parse("unreadable:1")!;
        Assert.False(checkoutPlan.TryConsumeSnapshotFault());
        Assert.Equal(1, checkoutPlan.Remaining);
        Assert.True(checkoutPlan.TryConsumeCheckoutFault(out var fault));
        Assert.Equal(InjectedCheckoutFault.Unreadable, fault);
    }

    // ── The environment read ──────────────────────────────────────────────────

    /// <summary>
    /// <see cref="CheckoutFaultPlan.FromEnvironment"/> is the only place the variable is read, and it is
    /// compiled out of Release builds: a DEBUG build parses it; a Release build returns null whatever the
    /// environment holds, so a stray variable can never change how a shipped build behaves. The test
    /// restores the variable (tests run without parallelism).
    /// </summary>
    [Fact]
    public void FromEnvironment_ReadsTheVariableInDebugBuildsOnly()
    {
        var original = Environment.GetEnvironmentVariable(CheckoutFaultPlan.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(CheckoutFaultPlan.EnvironmentVariableName, "snapshot:2");
            var plan = CheckoutFaultPlan.FromEnvironment();

#if DEBUG
            Assert.NotNull(plan);
            Assert.Equal(InjectedCheckoutFault.Snapshot, plan.Fault);
            Assert.Equal(2, plan.Remaining);
#else
            Assert.Null(plan);
#endif

            Environment.SetEnvironmentVariable(CheckoutFaultPlan.EnvironmentVariableName, "not-a-fault");
            Assert.Null(CheckoutFaultPlan.FromEnvironment());

            Environment.SetEnvironmentVariable(CheckoutFaultPlan.EnvironmentVariableName, null);
            Assert.Null(CheckoutFaultPlan.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CheckoutFaultPlan.EnvironmentVariableName, original);
        }
    }
}
