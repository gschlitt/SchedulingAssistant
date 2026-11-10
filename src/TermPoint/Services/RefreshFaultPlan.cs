namespace TermPoint.Services;

/// <summary>
/// The failure a <see cref="RefreshFaultPlan"/> injects into <c>CheckoutService.RefreshReadOnlySnapshotAsync</c>.
/// Each value names the real-world failure it stands in for.
/// </summary>
public enum InjectedRefreshFault
{
    /// <summary>No fault.</summary>
    None = 0,

    /// <summary>
    /// <c>replace</c> — the rename of the fresh copy over the read-only copy fails (the stand-in for Defender holding
    /// the old file): <see cref="RefreshOutcome.ReplaceFailed"/> when the reopen on the previous copy works.
    /// </summary>
    Replace,

    /// <summary>
    /// <c>reopen</c> — the reopen of the connection (the <c>afterOverwrite</c> call) fails. <c>reopen:2</c> recovers
    /// on the third attempt, <c>reopen:3</c> gives <see cref="RefreshOutcome.ConnectionLost"/>.
    /// </summary>
    Reopen
}

/// <summary>
/// A DEBUG-ONLY plan for making a read-only refresh fail on purpose, driven by the
/// <c>TERMPOINT_DEBUG_FAIL_REFRESH</c> environment variable, so the unhappy paths of the Refresh button
/// (spec item 4.6) can be exercised by hand.
///
/// <para><b>Syntax:</b> <c>value[:count[:skip]]</c>, case-insensitive, exactly as for
/// <see cref="RestoreFaultPlan"/> (parsing and budget are the shared <see cref="DebugFaultBudget"/>). <c>value</c> is
/// <c>replace</c> or <c>reopen</c> (see <see cref="InjectedRefreshFault"/>). For <c>reopen</c>, each reopen ATTEMPT
/// counts against the budget, so <c>reopen:2</c> fails two attempts and the third works.</para>
///
/// <para><b>Release builds ignore it entirely:</b> <see cref="FromEnvironment"/> is the only place the variable is
/// read and its body is compiled out of Release. <see cref="Parse"/> and <see cref="TryConsume"/> are pure, so they
/// are unit-tested in every configuration.</para>
/// </summary>
public sealed class RefreshFaultPlan
{
    /// <summary>The environment variable read (DEBUG builds only) by <see cref="FromEnvironment"/>.</summary>
    public const string EnvironmentVariableName = "TERMPOINT_DEBUG_FAIL_REFRESH";

    /// <summary>The shared, thread-safe count/skip budget.</summary>
    private readonly DebugFaultBudget _budget;

    /// <summary>Creates a plan. Use <see cref="Parse"/>; the constructor does not validate.</summary>
    /// <param name="fault">The fault to inject.</param>
    /// <param name="budget">How many matching calls fail, and how many are let through first.</param>
    private RefreshFaultPlan(InjectedRefreshFault fault, DebugFaultBudget budget)
    {
        Fault   = fault;
        _budget = budget;
    }

    /// <summary>The fault this plan injects (never <see cref="InjectedRefreshFault.None"/>).</summary>
    public InjectedRefreshFault Fault { get; }

    /// <summary>A short, human-readable budget for the log line (see <see cref="DebugFaultBudget.Description"/>).</summary>
    public string Description => _budget.Description;

    /// <summary>
    /// Parses <c>value[:count[:skip]]</c>. Pure: no environment access.
    /// </summary>
    /// <param name="spec">The raw text, e.g. <c>replace</c> or <c>reopen:2</c>; may be null or empty.</param>
    /// <returns>The plan, or null when <paramref name="spec"/> is empty or not understood (garbage is ignored).</returns>
    public static RefreshFaultPlan? Parse(string? spec)
    {
        if (!DebugFaultBudget.TryParseSpec(spec, out var name, out var budget))
            return null;

        var fault = name switch
        {
            "replace" => InjectedRefreshFault.Replace,
            "reopen"  => InjectedRefreshFault.Reopen,
            _         => InjectedRefreshFault.None
        };

        return fault == InjectedRefreshFault.None ? null : new RefreshFaultPlan(fault, budget);
    }

    /// <summary>
    /// Reads <see cref="EnvironmentVariableName"/> and parses it, in DEBUG builds only. In Release builds the read is
    /// compiled out and this always returns null.
    /// </summary>
    /// <returns>The plan, or null when the variable is unset, not understood, or this is a Release build.</returns>
    public static RefreshFaultPlan? FromEnvironment()
    {
#if DEBUG
        return Parse(Environment.GetEnvironmentVariable(EnvironmentVariableName));
#else
        return null;
#endif
    }

    /// <summary>
    /// Asks whether the refresh that has just reached <paramref name="stage"/> should fail there. Counts the call
    /// against the plan only when <paramref name="stage"/> is the plan's own fault.
    /// </summary>
    /// <param name="stage">The stage reached, named by the fault that targets it.</param>
    /// <returns>True when the refresh must fail at this stage.</returns>
    public bool TryConsume(InjectedRefreshFault stage)
        => stage != InjectedRefreshFault.None && Fault == stage && _budget.TryConsume();
}
