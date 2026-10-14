namespace TermPoint.Services;

/// <summary>
/// The failure a <see cref="CheckoutFaultPlan"/> injects. Each value names the real-world failure it
/// stands in for, so a developer can hand-test the unhappy paths (spec item 4d) that cannot be
/// reached otherwise — the database chooser that follows a failed switch, and the "Couldn't Open the
/// Database" prompt, which only a copy that fails AFTER validation can reach.
/// </summary>
public enum InjectedCheckoutFault
{
    /// <summary>No fault.</summary>
    None = 0,

    /// <summary><c>unreachable</c> — <c>CheckoutAsync</c> returns <see cref="CheckoutOutcome.NetworkUnreachable"/>.</summary>
    Unreachable,

    /// <summary><c>missing</c> — <c>CheckoutAsync</c> returns <see cref="CheckoutOutcome.SourceMissing"/>.</summary>
    Missing,

    /// <summary><c>unreadable</c> — <c>CheckoutAsync</c> returns <see cref="CheckoutOutcome.SourceUnreadable"/>.</summary>
    Unreadable,

    /// <summary>
    /// <c>snapshot</c> — <c>SetupReadOnlySnapshotAsync</c> returns null, as if the local read-only
    /// copy could not be made. Reach it through the observer setting, a licence-gated database or a
    /// lock held by someone else; the "Couldn't Open the Database" prompt then appears.
    /// </summary>
    Snapshot
}

/// <summary>
/// A DEBUG-ONLY plan for making checkouts fail on purpose, driven by the
/// <c>TERMPOINT_DEBUG_FAIL_CHECKOUT</c> environment variable. It exists so the failed-switch
/// recovery (spec item 4d) and the "Couldn't Open the Database" dialog can be exercised by hand:
/// every open runs the validator first, so a locked or missing file is refused before checkout is
/// ever reached and these paths are otherwise unreachable.
///
/// <para><b>Syntax:</b> <c>value[:count[:skip]]</c>, case-insensitive.</para>
/// <list type="bullet">
/// <item><description><c>value</c> — one of <c>unreachable</c>, <c>missing</c>, <c>unreadable</c>,
/// <c>snapshot</c> (see <see cref="InjectedCheckoutFault"/>). Alone, the fault fires on EVERY matching
/// call for as long as the plan exists — i.e. until TermPoint is restarted with the variable cleared.</description></item>
/// <item><description><c>count</c> — a whole number &gt;= 1, or <c>*</c> for "every call" (only useful to
/// reach <c>skip</c>). Only the next <c>count</c> matching calls fail: <c>unreachable:1</c> is "fail once,
/// then let the retry from the chooser succeed".</description></item>
/// <item><description><c>skip</c> — a whole number &gt;= 0: let that many matching calls through FIRST. The
/// app's own startup open is the first checkout of the process, so to fail a later database SWITCH
/// without failing startup use <c>unreachable:1:1</c> (let startup through, fail the next switch once)
/// or <c>unreachable:*:1</c> (let startup through, fail every switch after it).</description></item>
/// </list>
/// <para>Anything else — unknown names, a zero, negative, signed or non-numeric count or skip, extra
/// parts — is ignored and yields no plan, so a typo can never break a build that is being used for
/// real work.</para>
///
/// <para><b>Release builds ignore it entirely:</b> <see cref="FromEnvironment"/> is the only place
/// the variable is read and its body is compiled out of Release (the same pattern as
/// <c>TERMPOINT_INSTALL_CHANNEL</c> in <c>PlatformCapabilities</c>), so a stray variable on a customer
/// machine can never change how a shipped build behaves. <see cref="Parse"/> and the consume methods
/// are pure, so they are unit-tested in every configuration.</para>
///
/// <para>The <c>value[:count[:skip]]</c> parsing and the thread-safe count/skip budget live in
/// <see cref="DebugFaultBudget"/>, shared with <see cref="RestoreFaultPlan"/>; this class only maps the
/// <c>value</c> text to <see cref="InjectedCheckoutFault"/> and decides which call kinds a fault answers.</para>
///
/// <para>One plan holds one fault. It is consulted by
/// <see cref="CheckoutService.CheckoutAsync"/> (the three checkout faults) and
/// <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/> (the snapshot fault). Each fault counts only
/// its own kind of call.</para>
/// </summary>
public sealed class CheckoutFaultPlan
{
    /// <summary>The environment variable read (DEBUG builds only) by <see cref="FromEnvironment"/>.</summary>
    public const string EnvironmentVariableName = "TERMPOINT_DEBUG_FAIL_CHECKOUT";

    /// <summary>The shared count/skip budget (see <see cref="DebugFaultBudget"/>); checkouts and snapshot setups can overlap on thread-pool continuations, and it is thread-safe.</summary>
    private readonly DebugFaultBudget _budget;

    /// <summary>Creates a plan. Use <see cref="Parse"/>; the constructor does not validate.</summary>
    /// <param name="fault">The fault to inject.</param>
    /// <param name="budget">How many matching calls fail, and how many are let through first.</param>
    private CheckoutFaultPlan(InjectedCheckoutFault fault, DebugFaultBudget budget)
    {
        Fault   = fault;
        _budget = budget;
    }

    /// <summary>The fault this plan injects (never <see cref="InjectedCheckoutFault.None"/>).</summary>
    public InjectedCheckoutFault Fault { get; }

    /// <summary>How many more matching calls will fail (after the skipped ones), or null when the plan is unlimited.</summary>
    public int? Remaining => _budget.Remaining;

    /// <summary>How many more matching calls will be let through before failures begin.</summary>
    public int Skip => _budget.Skip;

    /// <summary>
    /// A short, human-readable budget for the log line written when the plan is created or fires:
    /// "fails every call…" or "N more call(s) will fail", plus any calls still to be let through. Read
    /// after a call has been consumed, so a counted plan's last firing reads "0 more call(s) will fail".
    /// </summary>
    public string Description => _budget.Description;

    /// <summary>
    /// Parses <c>value[:count[:skip]]</c> (see the class remarks). Pure: no environment access.
    /// </summary>
    /// <param name="spec">The raw text, e.g. <c>unreachable</c>, <c>snapshot:1</c> or <c>unreachable:1:1</c>; may be null or empty.</param>
    /// <returns>The plan, or null when <paramref name="spec"/> is empty or not understood (garbage is ignored).</returns>
    public static CheckoutFaultPlan? Parse(string? spec)
    {
        if (!DebugFaultBudget.TryParseSpec(spec, out var name, out var budget))
            return null;

        var fault = name switch
        {
            "unreachable" => InjectedCheckoutFault.Unreachable,
            "missing"     => InjectedCheckoutFault.Missing,
            "unreadable"  => InjectedCheckoutFault.Unreadable,
            "snapshot"    => InjectedCheckoutFault.Snapshot,
            _             => InjectedCheckoutFault.None
        };

        return fault == InjectedCheckoutFault.None ? null : new CheckoutFaultPlan(fault, budget);
    }

    /// <summary>
    /// Reads <see cref="EnvironmentVariableName"/> and parses it — in DEBUG builds only. In Release
    /// builds the read is compiled out and this always returns null, whatever the environment holds.
    /// This is the single place the variable is read.
    /// </summary>
    /// <returns>The plan, or null when the variable is unset, not understood, or this is a Release build.</returns>
    public static CheckoutFaultPlan? FromEnvironment()
    {
#if DEBUG
        return Parse(Environment.GetEnvironmentVariable(EnvironmentVariableName));
#else
        return null;
#endif
    }

    /// <summary>
    /// Asks whether the current <c>CheckoutAsync</c> call should fail, and if so with which fault. Counts
    /// the call against the plan (skip first, then the failure budget). Only the three checkout faults answer here.
    /// </summary>
    /// <param name="fault">The fault to inject when this returns true; otherwise <see cref="InjectedCheckoutFault.None"/>.</param>
    /// <returns>True when this checkout must fail.</returns>
    public bool TryConsumeCheckoutFault(out InjectedCheckoutFault fault)
    {
        fault = InjectedCheckoutFault.None;
        if (Fault is not (InjectedCheckoutFault.Unreachable or InjectedCheckoutFault.Missing or InjectedCheckoutFault.Unreadable))
            return false;
        if (!_budget.TryConsume())
            return false;

        fault = Fault;
        return true;
    }

    /// <summary>
    /// Asks whether the current attempt to make the local read-only copy should fail. Counts the
    /// attempt against the plan (skip first, then the failure budget). Only the
    /// <see cref="InjectedCheckoutFault.Snapshot"/> fault answers here.
    /// </summary>
    /// <returns>True when this snapshot attempt must fail.</returns>
    public bool TryConsumeSnapshotFault()
        => Fault == InjectedCheckoutFault.Snapshot && _budget.TryConsume();
}
