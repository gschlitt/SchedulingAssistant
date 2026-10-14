namespace TermPoint.Services;

/// <summary>
/// The failure an <see cref="IntegrityFaultPlan"/> injects. Each value names the real-world failure it stands in
/// for, so a developer can hand-test the unhappy paths of the open-time check of the local copy (spec item 5,
/// step 5d), none of which can be produced on demand against a healthy machine.
/// </summary>
public enum InjectedIntegrityFault
{
    /// <summary>No fault.</summary>
    None = 0,

    /// <summary>
    /// <c>locked</c> — the open-time check of the local copy (D' or D'') reports that the file could not be read
    /// (<see cref="IntegrityCheckResult.Unreadable"/>), as if antivirus held it. Every quiet retry is a separate
    /// call, so <c>locked:3</c> makes one whole open fail (all three attempts) and the next one succeed.
    /// </summary>
    Locked,

    /// <summary>
    /// <c>damaged</c> — the open-time check of the local copy reports damage
    /// (<see cref="IntegrityCheckResult.Damaged"/>). <c>damaged:1</c>: the fresh copy that follows passes, so the
    /// open recovers. <c>damaged:2</c>: the fresh copy fails too, so the "keeps failing" message appears.
    /// </summary>
    Damaged,

    /// <summary>
    /// <c>shared</c> — the handler's re-check of the shared database D reports damage, so a damaged local copy is
    /// not "healed" by a fresh copy but sends the user to the recovery chooser. The re-check is only reached when
    /// the local copy has failed its check, so — to make this fault usable on its own — a <c>shared</c> plan ALSO
    /// makes the open-time check of the local copy report damage for as long as the plan has failures left (the
    /// story it tells is "the shared database is damaged, and so is the copy made from it"). <c>shared:1</c>: the
    /// first open fails that way and the re-check spends the budget, so the open the user picks from the recovery
    /// chooser passes.
    /// </summary>
    Shared
}

/// <summary>
/// A DEBUG-ONLY plan for making the open-time integrity check fail on purpose, driven by the
/// <c>TERMPOINT_DEBUG_FAIL_INTEGRITY</c> environment variable. It exists so the handling of a locked or damaged
/// local copy (spec item 5, step 5d) can be exercised by hand: a copy that antivirus holds, a copy that is
/// damaged while the shared database is fine, a copy that keeps failing, and a shared database that is damaged.
///
/// <para><b>Syntax:</b> <c>value[:count[:skip]]</c>, case-insensitive, exactly as for
/// <see cref="CheckoutFaultPlan"/> and <see cref="RestoreFaultPlan"/> (the parsing and the budget are the shared
/// <see cref="DebugFaultBudget"/>).</para>
/// <list type="bullet">
/// <item><description><c>value</c> — one of <c>locked</c>, <c>damaged</c>, <c>shared</c> (see
/// <see cref="InjectedIntegrityFault"/>). Alone, the fault fires on EVERY call that reaches its stage, until
/// TermPoint is restarted with the variable cleared.</description></item>
/// <item><description><c>count</c> — a whole number &gt;= 1, or <c>*</c> for "every call": only the next
/// <c>count</c> calls fail.</description></item>
/// <item><description><c>skip</c> — a whole number &gt;= 0: let that many calls through first.</description></item>
/// </list>
/// <para>Anything else is ignored and yields no plan, so a typo can never break a build that is being used for
/// real work. One plan holds one fault, and a fault counts only calls at its own stage: <c>locked</c> and
/// <c>damaged</c> count open-time checks of the local copy; <c>shared</c> counts re-checks of the shared
/// database (and, so that the re-check is ever reached, also makes the local copy's check report damage while the
/// plan has failures left — see <see cref="InjectedIntegrityFault.Shared"/>).</para>
///
/// <para><b>Release builds ignore it entirely:</b> <see cref="FromEnvironment"/> is the only place the variable
/// is read and its body is compiled out of Release (the same pattern as <see cref="RestoreFaultPlan"/>), so a
/// stray variable on a customer machine can never change how a shipped build behaves. <see cref="Parse"/> and
/// <see cref="TryConsume"/> are pure, so they are unit-tested in every configuration.</para>
/// </summary>
public sealed class IntegrityFaultPlan
{
    /// <summary>The environment variable read (DEBUG builds only) by <see cref="FromEnvironment"/>.</summary>
    public const string EnvironmentVariableName = "TERMPOINT_DEBUG_FAIL_INTEGRITY";

    /// <summary>The shared, thread-safe count/skip budget.</summary>
    private readonly DebugFaultBudget _budget;

    /// <summary>Creates a plan. Use <see cref="Parse"/>; the constructor does not validate.</summary>
    /// <param name="fault">The fault to inject.</param>
    /// <param name="budget">How many matching calls fail, and how many are let through first.</param>
    private IntegrityFaultPlan(InjectedIntegrityFault fault, DebugFaultBudget budget)
    {
        Fault   = fault;
        _budget = budget;
    }

    /// <summary>The fault this plan injects (never <see cref="InjectedIntegrityFault.None"/>).</summary>
    public InjectedIntegrityFault Fault { get; }

    /// <summary>How many more matching calls will fail (after the skipped ones), or null when the plan is unlimited.</summary>
    public int? Remaining => _budget.Remaining;

    /// <summary>How many more matching calls will be let through before failures begin.</summary>
    public int Skip => _budget.Skip;

    /// <summary>
    /// True while the plan can still fail a call: it is unlimited, or its failure budget is not used up. Read-only —
    /// asking does not spend anything (unlike <see cref="TryConsume"/>).
    /// </summary>
    public bool HasFailuresLeft => Remaining is null || Remaining > 0;

    /// <summary>
    /// A short, human-readable budget for the log line written when the plan is created or fires
    /// (see <see cref="DebugFaultBudget.Description"/>).
    /// </summary>
    public string Description => _budget.Description;

    /// <summary>
    /// Parses <c>value[:count[:skip]]</c> (see the class remarks). Pure: no environment access.
    /// </summary>
    /// <param name="spec">The raw text, e.g. <c>locked</c>, <c>damaged:1</c> or <c>locked:3:1</c>; may be null or empty.</param>
    /// <returns>The plan, or null when <paramref name="spec"/> is empty or not understood (garbage is ignored).</returns>
    public static IntegrityFaultPlan? Parse(string? spec)
    {
        if (!DebugFaultBudget.TryParseSpec(spec, out var name, out var budget))
            return null;

        var fault = name switch
        {
            "locked"  => InjectedIntegrityFault.Locked,
            "damaged" => InjectedIntegrityFault.Damaged,
            "shared"  => InjectedIntegrityFault.Shared,
            _         => InjectedIntegrityFault.None
        };

        return fault == InjectedIntegrityFault.None ? null : new IntegrityFaultPlan(fault, budget);
    }

    /// <summary>
    /// Reads <see cref="EnvironmentVariableName"/> and parses it — in DEBUG builds only. In Release builds the
    /// read is compiled out and this always returns null, whatever the environment holds. This is the single
    /// place the variable is read.
    /// </summary>
    /// <returns>The plan, or null when the variable is unset, not understood, or this is a Release build.</returns>
    public static IntegrityFaultPlan? FromEnvironment()
    {
#if DEBUG
        return Parse(Environment.GetEnvironmentVariable(EnvironmentVariableName));
#else
        return null;
#endif
    }

    /// <summary>
    /// Asks whether the call that has just reached <paramref name="stage"/> should fail there. Counts the call
    /// against the plan (skip first, then the failure budget) — but only when <paramref name="stage"/> is the
    /// plan's own fault; a call at any other stage neither fails nor spends the budget.
    /// </summary>
    /// <param name="stage">The stage the call has reached, named by the fault that targets it.</param>
    /// <returns>True when the call must fail at this stage.</returns>
    public bool TryConsume(InjectedIntegrityFault stage)
        => stage != InjectedIntegrityFault.None && Fault == stage && _budget.TryConsume();
}
