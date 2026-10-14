using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="RestoreFaultPlan"/>, the DEBUG-ONLY fault injection behind the
/// <c>TERMPOINT_DEBUG_FAIL_RESTORE</c> environment variable (spec item 5, step 5a). The parser and the budget
/// accounting are pure and run in every configuration; they are the same shared machinery as
/// <see cref="CheckoutFaultPlan"/> (<see cref="DebugFaultBudget"/>), covered for that plan by
/// <see cref="CheckoutFaultPlanTests"/>. Only the environment read is configuration-dependent: it is compiled
/// out of Release builds.
///
/// <para>How a plan makes <see cref="DatabaseRestore"/> fail at each stage is covered in
/// <see cref="DatabaseRestoreTests"/>.</para>
/// </summary>
public sealed class RestoreFaultPlanTests
{
    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Each documented value yields a plan for the matching fault, case-insensitively and ignoring surrounding
    /// whitespace, and without a count it is unlimited.
    /// </summary>
    [Theory]
    [InlineData("backup",    InjectedRestoreFault.Backup)]
    [InlineData("damaged",   InjectedRestoreFault.Damaged)]
    [InlineData("precopy",   InjectedRestoreFault.PreCopy)]
    [InlineData("copy",      InjectedRestoreFault.Copy)]
    [InlineData("verify",    InjectedRestoreFault.Verify)]
    [InlineData("lock",      InjectedRestoreFault.Lock)]
    [InlineData("rename",    InjectedRestoreFault.Rename)]
    [InlineData("COPY",      InjectedRestoreFault.Copy)]
    [InlineData("  Rename  ", InjectedRestoreFault.Rename)]
    public void Parse_KnownValue_ReturnsUnlimitedPlan(string spec, InjectedRestoreFault expected)
    {
        var plan = RestoreFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Null(plan.Remaining);
        Assert.Equal(0, plan.Skip);
    }

    /// <summary>A <c>value:count</c> form limits the plan to that many calls.</summary>
    [Theory]
    [InlineData("copy:1",       InjectedRestoreFault.Copy,    1)]
    [InlineData("damaged:3",    InjectedRestoreFault.Damaged, 3)]
    [InlineData(" Lock : 2 ",   InjectedRestoreFault.Lock,    2)]
    [InlineData("rename:10",    InjectedRestoreFault.Rename,  10)]
    public void Parse_ValueWithCount_ReturnsCountedPlan(string spec, InjectedRestoreFault expected, int count)
    {
        var plan = RestoreFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Equal(count, plan.Remaining);
    }

    /// <summary>The <c>value:count:skip</c> form lets the first <c>skip</c> matching calls through; <c>*</c> means "every call".</summary>
    [Theory]
    [InlineData("copy:1:1",         InjectedRestoreFault.Copy,   1,    1)]
    [InlineData("copy:*:1",         InjectedRestoreFault.Copy,   null, 1)]
    [InlineData("verify:2:0",       InjectedRestoreFault.Verify, 2,    0)]
    [InlineData(" Backup : * : 3 ", InjectedRestoreFault.Backup, null, 3)]
    [InlineData("precopy:*",        InjectedRestoreFault.PreCopy, null, 0)]
    public void Parse_ValueWithCountAndSkip_ReturnsPlanThatSkipsFirst(string spec, InjectedRestoreFault expected, int? count, int skip)
    {
        var plan = RestoreFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Equal(count, plan.Remaining);
        Assert.Equal(skip, plan.Skip);
    }

    /// <summary>
    /// Garbage is ignored — no plan, no exception: unknown names (including the CHECKOUT plan's values), empty
    /// text, extra parts, and a count or skip that is missing, zero (for the count), negative, signed,
    /// fractional or not a number.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    [InlineData("1")]
    [InlineData("none")]
    [InlineData("unreachable")]
    [InlineData("snapshot:1")]
    [InlineData(":2")]
    [InlineData("copy:")]
    [InlineData("copy:0")]
    [InlineData("copy:-1")]
    [InlineData("copy:+1")]
    [InlineData("copy:1.5")]
    [InlineData("copy:abc")]
    [InlineData("copy:1:2:3")]
    [InlineData("copy:1:")]
    [InlineData("copy:1:-1")]
    [InlineData("copy:1:abc")]
    [InlineData("copy::1")]
    [InlineData("copy:**")]
    [InlineData("copy:*:x")]
    [InlineData("copy:99999999999")]
    [InlineData("copy,lock")]
    public void Parse_Garbage_ReturnsNull(string? spec)
    {
        Assert.Null(RestoreFaultPlan.Parse(spec));
    }

    // ── Consumption ───────────────────────────────────────────────────────────

    /// <summary>
    /// A fault answers only at its own stage: a call at any other stage neither fails nor spends the budget,
    /// and <see cref="InjectedRestoreFault.None"/> never fails.
    /// </summary>
    [Fact]
    public void TryConsume_OnlyAnswersItsOwnStage_AndDoesNotSpendTheBudgetOnOthers()
    {
        var plan = RestoreFaultPlan.Parse("copy:1")!;

        foreach (var other in Enum.GetValues<InjectedRestoreFault>().Where(f => f != InjectedRestoreFault.Copy))
            Assert.False(plan.TryConsume(other));
        Assert.Equal(1, plan.Remaining);

        Assert.True(plan.TryConsume(InjectedRestoreFault.Copy));
        Assert.Equal(0, plan.Remaining);
        Assert.False(plan.TryConsume(InjectedRestoreFault.Copy));
    }

    /// <summary>An unlimited plan fires on every call that reaches its stage.</summary>
    [Fact]
    public void TryConsume_Unlimited_FiresEveryTime()
    {
        var plan = RestoreFaultPlan.Parse("lock")!;

        for (var i = 0; i < 25; i++)
            Assert.True(plan.TryConsume(InjectedRestoreFault.Lock));

        Assert.Null(plan.Remaining);
    }

    /// <summary>A counted plan fails exactly that many calls, then lets every later one through.</summary>
    [Fact]
    public void TryConsume_Counted_FiresExactlyNTimes()
    {
        var plan = RestoreFaultPlan.Parse("verify:2")!;

        Assert.True(plan.TryConsume(InjectedRestoreFault.Verify));
        Assert.Equal(1, plan.Remaining);
        Assert.True(plan.TryConsume(InjectedRestoreFault.Verify));
        Assert.Equal(0, plan.Remaining);
        Assert.False(plan.TryConsume(InjectedRestoreFault.Verify));
        Assert.False(plan.TryConsume(InjectedRestoreFault.Verify));
    }

    /// <summary><c>copy:1:1</c>: the first restore to reach the stage is let through, the second fails, every later one is let through.</summary>
    [Fact]
    public void TryConsume_CountedWithSkip_LetsTheFirstThrough_ThenFailsExactlyN()
    {
        var plan = RestoreFaultPlan.Parse("copy:1:1")!;

        Assert.False(plan.TryConsume(InjectedRestoreFault.Copy));
        Assert.Equal(0, plan.Skip);
        Assert.Equal(1, plan.Remaining);

        Assert.True(plan.TryConsume(InjectedRestoreFault.Copy));
        Assert.False(plan.TryConsume(InjectedRestoreFault.Copy));
    }

    /// <summary>The log text describes the budget in the same words as the checkout plan.</summary>
    [Fact]
    public void Description_DescribesTheBudget()
    {
        Assert.Equal("fails every call until the variable is cleared and TermPoint restarted",
            RestoreFaultPlan.Parse("copy")!.Description);

        var counted = RestoreFaultPlan.Parse("copy:2")!;
        Assert.Equal("2 more call(s) will fail", counted.Description);
        counted.TryConsume(InjectedRestoreFault.Copy);
        counted.TryConsume(InjectedRestoreFault.Copy);
        Assert.Equal("0 more call(s) will fail", counted.Description);

        Assert.Equal("1 call(s) will be let through first, then it 1 more call(s) will fail",
            RestoreFaultPlan.Parse("copy:1:1")!.Description);
    }

    // ── The environment read ──────────────────────────────────────────────────

    /// <summary>The variable name is the documented one.</summary>
    [Fact]
    public void EnvironmentVariableName_IsTheDocumentedOne()
    {
        Assert.Equal("TERMPOINT_DEBUG_FAIL_RESTORE", RestoreFaultPlan.EnvironmentVariableName);
        Assert.NotEqual(CheckoutFaultPlan.EnvironmentVariableName, RestoreFaultPlan.EnvironmentVariableName);
    }

    /// <summary>
    /// <see cref="RestoreFaultPlan.FromEnvironment"/> is the only place the variable is read, and it is compiled
    /// out of Release builds: a DEBUG build parses it; a Release build returns null whatever the environment
    /// holds. The test restores the variable (tests run without parallelism).
    /// </summary>
    [Fact]
    public void FromEnvironment_ReadsTheVariableInDebugBuildsOnly()
    {
        var original = Environment.GetEnvironmentVariable(RestoreFaultPlan.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(RestoreFaultPlan.EnvironmentVariableName, "copy:2");
            var plan = RestoreFaultPlan.FromEnvironment();

#if DEBUG
            Assert.NotNull(plan);
            Assert.Equal(InjectedRestoreFault.Copy, plan.Fault);
            Assert.Equal(2, plan.Remaining);
#else
            Assert.Null(plan);
#endif

            Environment.SetEnvironmentVariable(RestoreFaultPlan.EnvironmentVariableName, "not-a-fault");
            Assert.Null(RestoreFaultPlan.FromEnvironment());

            Environment.SetEnvironmentVariable(RestoreFaultPlan.EnvironmentVariableName, null);
            Assert.Null(RestoreFaultPlan.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(RestoreFaultPlan.EnvironmentVariableName, original);
        }
    }
}
