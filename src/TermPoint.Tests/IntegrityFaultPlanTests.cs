using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="IntegrityFaultPlan"/>, the DEBUG-ONLY fault injection behind the
/// <c>TERMPOINT_DEBUG_FAIL_INTEGRITY</c> environment variable (spec item 5, step 5d). The parser and the budget
/// accounting are pure and run in every configuration; they are the same shared machinery as
/// <see cref="RestoreFaultPlan"/> (<see cref="DebugFaultBudget"/>). Only the environment read is
/// configuration-dependent: it is compiled out of Release builds.
///
/// <para>How the plan makes the open-time check and the shared-database re-check fail is covered in
/// <see cref="LocalCopyIntegrityTests"/>.</para>
/// </summary>
public sealed class IntegrityFaultPlanTests
{
    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Each documented value yields a plan for the matching fault, case-insensitively and ignoring surrounding
    /// whitespace, and without a count it is unlimited.
    /// </summary>
    [Theory]
    [InlineData("locked",     InjectedIntegrityFault.Locked)]
    [InlineData("damaged",    InjectedIntegrityFault.Damaged)]
    [InlineData("shared",     InjectedIntegrityFault.Shared)]
    [InlineData("stamp",      InjectedIntegrityFault.Stamp)]
    [InlineData("LOCKED",     InjectedIntegrityFault.Locked)]
    [InlineData("  Shared  ", InjectedIntegrityFault.Shared)]
    [InlineData("STAMP",      InjectedIntegrityFault.Stamp)]
    [InlineData("  Stamp  ",  InjectedIntegrityFault.Stamp)]
    public void Parse_KnownValue_ReturnsUnlimitedPlan(string spec, InjectedIntegrityFault expected)
    {
        var plan = IntegrityFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Null(plan.Remaining);
        Assert.Equal(0, plan.Skip);
    }

    /// <summary>A <c>value:count</c> form limits the plan to that many calls.</summary>
    [Theory]
    [InlineData("locked:3",   InjectedIntegrityFault.Locked,  3)]
    [InlineData("damaged:1",  InjectedIntegrityFault.Damaged, 1)]
    [InlineData("damaged:2",  InjectedIntegrityFault.Damaged, 2)]
    [InlineData(" Shared : 1 ", InjectedIntegrityFault.Shared, 1)]
    [InlineData("stamp:3",    InjectedIntegrityFault.Stamp,   3)]
    [InlineData("stamp:2",    InjectedIntegrityFault.Stamp,   2)]
    [InlineData(" STAMP : 3 ", InjectedIntegrityFault.Stamp,  3)]
    public void Parse_ValueWithCount_ReturnsCountedPlan(string spec, InjectedIntegrityFault expected, int count)
    {
        var plan = IntegrityFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Equal(count, plan.Remaining);
    }

    /// <summary>The <c>value:count:skip</c> form lets the first <c>skip</c> matching calls through; <c>*</c> means "every call".</summary>
    [Theory]
    [InlineData("locked:3:1",  InjectedIntegrityFault.Locked,  3,    1)]
    [InlineData("damaged:*:1", InjectedIntegrityFault.Damaged, null, 1)]
    [InlineData("shared:1:0",  InjectedIntegrityFault.Shared,  1,    0)]
    [InlineData("stamp:3:1",   InjectedIntegrityFault.Stamp,   3,    1)]
    [InlineData("stamp:*:2",   InjectedIntegrityFault.Stamp,   null, 2)]
    public void Parse_ValueWithCountAndSkip_ReturnsPlanThatSkipsFirst(string spec, InjectedIntegrityFault expected, int? count, int skip)
    {
        var plan = IntegrityFaultPlan.Parse(spec);

        Assert.NotNull(plan);
        Assert.Equal(expected, plan.Fault);
        Assert.Equal(count, plan.Remaining);
        Assert.Equal(skip, plan.Skip);
    }

    /// <summary>
    /// Garbage is ignored — no plan, no exception: unknown names (including the other plans' values), empty text,
    /// extra parts, and a count or skip that is zero, negative, signed, fractional or not a number.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    [InlineData("1")]
    [InlineData("none")]
    [InlineData("unreachable")]
    [InlineData("copy")]
    [InlineData("snapshot:1")]
    [InlineData("locked:0")]
    [InlineData("locked:-1")]
    [InlineData("locked:+1")]
    [InlineData("locked:1.5")]
    [InlineData("locked:abc")]
    [InlineData("locked:1:2:3")]
    [InlineData("locked:1:-1")]
    [InlineData("damaged:")]
    [InlineData("damaged,locked")]
    [InlineData("stamp:0")]
    [InlineData("stamp:-1")]
    [InlineData("stamp:abc")]
    [InlineData("stamped")]
    public void Parse_Garbage_ReturnsNull(string? spec)
    {
        Assert.Null(IntegrityFaultPlan.Parse(spec));
    }

    // ── Consumption ───────────────────────────────────────────────────────────

    /// <summary>
    /// A fault answers only at its own stage: a call at any other stage neither fails nor spends the budget, and
    /// <see cref="InjectedIntegrityFault.None"/> never fails.
    /// </summary>
    [Fact]
    public void TryConsume_OnlyAnswersItsOwnStage_AndDoesNotSpendTheBudgetOnOthers()
    {
        var plan = IntegrityFaultPlan.Parse("shared:1")!;

        foreach (var other in Enum.GetValues<InjectedIntegrityFault>().Where(f => f != InjectedIntegrityFault.Shared))
            Assert.False(plan.TryConsume(other));
        Assert.Equal(1, plan.Remaining);

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Shared));
        Assert.Equal(0, plan.Remaining);
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Shared));
    }

    /// <summary>An unlimited plan fires on every call that reaches its stage.</summary>
    [Fact]
    public void TryConsume_Unlimited_FiresEveryTime()
    {
        var plan = IntegrityFaultPlan.Parse("locked")!;

        for (var i = 0; i < 25; i++)
            Assert.True(plan.TryConsume(InjectedIntegrityFault.Locked));

        Assert.Null(plan.Remaining);
    }

    /// <summary>
    /// <c>locked:3</c>: exactly three calls fail — one whole open's worth of quiet attempts — and every later one
    /// is let through, so "Try again" then succeeds.
    /// </summary>
    [Fact]
    public void TryConsume_LockedThree_FailsExactlyThreeCalls()
    {
        var plan = IntegrityFaultPlan.Parse("locked:3")!;

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.Equal(0, plan.Remaining);
    }

    /// <summary><c>damaged:1</c> fails the first open-time check only (the fresh copy then passes).</summary>
    [Fact]
    public void TryConsume_DamagedOne_FailsOnlyTheFirstCheck()
    {
        var plan = IntegrityFaultPlan.Parse("damaged:1")!;

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Damaged));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Damaged));
    }

    /// <summary><c>damaged:2</c> fails the first two checks (the fresh copy fails too: "keeps failing").</summary>
    [Fact]
    public void TryConsume_DamagedTwo_FailsTheFirstTwoChecks()
    {
        var plan = IntegrityFaultPlan.Parse("damaged:2")!;

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Damaged));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Damaged));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Damaged));
    }

    /// <summary>
    /// <c>stamp:3</c> (spec item 3, step 3c): exactly three stamp reads fail — one whole checkout's worth of the
    /// version gate's quiet attempts — and every later one is let through, so "Try again" then succeeds.
    /// </summary>
    [Fact]
    public void TryConsume_StampThree_FailsExactlyThreeReads()
    {
        var plan = IntegrityFaultPlan.Parse("stamp:3")!;

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.Equal(0, plan.Remaining);
    }

    /// <summary><c>stamp:2</c> fails two reads, which the gate's third attempt absorbs.</summary>
    [Fact]
    public void TryConsume_StampTwo_FailsTwoReadsThenPasses()
    {
        var plan = IntegrityFaultPlan.Parse("stamp:2")!;

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Stamp));
    }

    /// <summary>
    /// The stamp fault answers only the version gate's read: the open-time check stages do not spend its budget,
    /// and a stamp read does not spend theirs.
    /// </summary>
    [Fact]
    public void TryConsume_StampFault_IsIndependentOfTheOtherStages()
    {
        var plan = IntegrityFaultPlan.Parse("stamp:1")!;

        Assert.False(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Damaged));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Shared));
        Assert.Equal(1, plan.Remaining);

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Stamp));

        var locked = IntegrityFaultPlan.Parse("locked:1")!;
        Assert.False(locked.TryConsume(InjectedIntegrityFault.Stamp));
        Assert.Equal(1, locked.Remaining);
    }

    /// <summary>A skip lets the first matching calls through: <c>locked:1:1</c> lets one check pass, fails the next, then passes.</summary>
    [Fact]
    public void TryConsume_CountedWithSkip_LetsTheFirstThrough_ThenFailsExactlyN()
    {
        var plan = IntegrityFaultPlan.Parse("locked:1:1")!;

        Assert.False(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.True(plan.TryConsume(InjectedIntegrityFault.Locked));
        Assert.False(plan.TryConsume(InjectedIntegrityFault.Locked));
    }

    /// <summary>The log text describes the budget in the same words as the other plans.</summary>
    [Fact]
    public void Description_DescribesTheBudget()
    {
        Assert.Equal("fails every call until the variable is cleared and TermPoint restarted",
            IntegrityFaultPlan.Parse("locked")!.Description);

        var counted = IntegrityFaultPlan.Parse("damaged:2")!;
        Assert.Equal("2 more call(s) will fail", counted.Description);
        counted.TryConsume(InjectedIntegrityFault.Damaged);
        Assert.Equal("1 more call(s) will fail", counted.Description);
    }

    // ── The environment read ──────────────────────────────────────────────────

    /// <summary>The variable name is the documented one, and distinct from the other plans'.</summary>
    [Fact]
    public void EnvironmentVariableName_IsTheDocumentedOne()
    {
        Assert.Equal("TERMPOINT_DEBUG_FAIL_INTEGRITY", IntegrityFaultPlan.EnvironmentVariableName);
        Assert.NotEqual(CheckoutFaultPlan.EnvironmentVariableName, IntegrityFaultPlan.EnvironmentVariableName);
        Assert.NotEqual(RestoreFaultPlan.EnvironmentVariableName, IntegrityFaultPlan.EnvironmentVariableName);
    }

    /// <summary>
    /// <see cref="IntegrityFaultPlan.FromEnvironment"/> is the only place the variable is read, and it is compiled
    /// out of Release builds: in a Release build it returns null whatever the environment holds; in a Debug build
    /// it parses the variable.
    /// </summary>
    [Fact]
    public void FromEnvironment_ReadsTheVariableInDebugBuilds_AndIsInertInRelease()
    {
        var original = Environment.GetEnvironmentVariable(IntegrityFaultPlan.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(IntegrityFaultPlan.EnvironmentVariableName, "damaged:2");

            var plan = IntegrityFaultPlan.FromEnvironment();

#if DEBUG
            Assert.NotNull(plan);
            Assert.Equal(InjectedIntegrityFault.Damaged, plan.Fault);
            Assert.Equal(2, plan.Remaining);
#else
            Assert.Null(plan);
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrityFaultPlan.EnvironmentVariableName, original);
        }
    }

    /// <summary>With the variable unset there is no plan.</summary>
    [Fact]
    public void FromEnvironment_Unset_ReturnsNull()
    {
        var original = Environment.GetEnvironmentVariable(IntegrityFaultPlan.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(IntegrityFaultPlan.EnvironmentVariableName, null);
            Assert.Null(IntegrityFaultPlan.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrityFaultPlan.EnvironmentVariableName, original);
        }
    }
}
