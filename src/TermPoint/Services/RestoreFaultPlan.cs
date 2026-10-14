namespace TermPoint.Services;

/// <summary>
/// The stage of <see cref="DatabaseRestore.ReplaceAsync"/> that a <see cref="RestoreFaultPlan"/> fails.
/// Each value names the real-world failure it stands in for, so a developer can hand-test the unhappy
/// paths of a restore (spec item 5) that cannot be reached on demand — above all "the network was cut
/// mid-restore", which is <see cref="Copy"/>.
/// </summary>
public enum InjectedRestoreFault
{
    /// <summary>No fault.</summary>
    None = 0,

    /// <summary><c>backup</c> — staging the backup (stage 2) stalls: <see cref="RestoreOutcome.Unreachable"/>.</summary>
    Backup,

    /// <summary><c>damaged</c> — the integrity check of the staged backup (stage 3) reports damage: <see cref="RestoreOutcome.BackupDamaged"/>.</summary>
    Damaged,

    /// <summary><c>precopy</c> — the pre-restore copy beside D (stage 5) stalls: <see cref="RestoreOutcome.Unreachable"/>.</summary>
    PreCopy,

    /// <summary>
    /// <c>copy</c> — streaming the backup to D.tmp (stage 6) stalls: <see cref="RestoreOutcome.Unreachable"/>.
    /// The stand-in for "the network was cut mid-restore": D must be untouched afterwards.
    /// </summary>
    Copy,

    /// <summary><c>verify</c> — D.tmp does not hash like the staged backup (stage 7, a torn transfer): <see cref="RestoreOutcome.Failed"/>.</summary>
    Verify,

    /// <summary><c>lock</c> — the last lock check before the swap (stage 8) says the lock is no longer ours: <see cref="RestoreOutcome.LockLost"/>.</summary>
    Lock,

    /// <summary>
    /// <c>rename</c> — the swap (stage 9) is not performed and behaves as a move that timed out; the probe
    /// then sees the old D, so the outcome is <see cref="RestoreOutcome.RenameUnconfirmed"/> with D unchanged,
    /// D.tmp left in place and the pre-restore copy kept.
    /// </summary>
    Rename
}

/// <summary>
/// A DEBUG-ONLY plan for making a restore fail on purpose, driven by the
/// <c>TERMPOINT_DEBUG_FAIL_RESTORE</c> environment variable. It exists so the failure handling around
/// <see cref="DatabaseRestore"/> (spec item 5) can be exercised by hand — a severed network mid-restore,
/// a damaged backup, a lost lock, an unconfirmed rename — none of which can be produced on demand
/// against a healthy share.
///
/// <para><b>Syntax:</b> <c>value[:count[:skip]]</c>, case-insensitive, exactly as for
/// <see cref="CheckoutFaultPlan"/> (the parsing and the budget are the shared <see cref="DebugFaultBudget"/>).</para>
/// <list type="bullet">
/// <item><description><c>value</c> — one of <c>backup</c>, <c>damaged</c>, <c>precopy</c>, <c>copy</c>,
/// <c>verify</c>, <c>lock</c>, <c>rename</c> (see <see cref="InjectedRestoreFault"/>). Alone, the fault fires on
/// EVERY restore that reaches its stage, until TermPoint is restarted with the variable cleared.</description></item>
/// <item><description><c>count</c> — a whole number &gt;= 1, or <c>*</c> for "every call": only the next
/// <c>count</c> restores that reach the stage fail (<c>copy:1</c> is "cut the network once, then let the
/// retry succeed").</description></item>
/// <item><description><c>skip</c> — a whole number &gt;= 0: let that many restores through the stage first.</description></item>
/// </list>
/// <para>Anything else is ignored and yields no plan, so a typo can never break a build that is being used
/// for real work.</para>
///
/// <para>A fault fires only at its own stage, and only calls that REACH that stage count against its budget
/// (a restore refused earlier, for another reason, does not spend it). One plan holds one fault. The create path
/// (<see cref="DatabaseRestore.CreateFromBackupAsync"/>, a restore into a destination that does not exist yet) has
/// no lock, pre-restore copy, verification or rename stage: only <c>backup</c>, <c>damaged</c> and <c>copy</c>
/// fire there.</para>
///
/// <para><b>Release builds ignore it entirely:</b> <see cref="FromEnvironment"/> is the only place the
/// variable is read and its body is compiled out of Release (the same pattern as
/// <see cref="CheckoutFaultPlan"/>), so a stray variable on a customer machine can never change how a
/// shipped build behaves. <see cref="Parse"/> and <see cref="TryConsume"/> are pure, so they are
/// unit-tested in every configuration.</para>
/// </summary>
public sealed class RestoreFaultPlan
{
    /// <summary>The environment variable read (DEBUG builds only) by <see cref="FromEnvironment"/>.</summary>
    public const string EnvironmentVariableName = "TERMPOINT_DEBUG_FAIL_RESTORE";

    /// <summary>The shared, thread-safe count/skip budget.</summary>
    private readonly DebugFaultBudget _budget;

    /// <summary>Creates a plan. Use <see cref="Parse"/>; the constructor does not validate.</summary>
    /// <param name="fault">The fault to inject.</param>
    /// <param name="budget">How many matching calls fail, and how many are let through first.</param>
    private RestoreFaultPlan(InjectedRestoreFault fault, DebugFaultBudget budget)
    {
        Fault   = fault;
        _budget = budget;
    }

    /// <summary>The fault this plan injects (never <see cref="InjectedRestoreFault.None"/>).</summary>
    public InjectedRestoreFault Fault { get; }

    /// <summary>How many more matching calls will fail (after the skipped ones), or null when the plan is unlimited.</summary>
    public int? Remaining => _budget.Remaining;

    /// <summary>How many more matching calls will be let through before failures begin.</summary>
    public int Skip => _budget.Skip;

    /// <summary>
    /// A short, human-readable budget for the log line written when the plan is created or fires
    /// (see <see cref="DebugFaultBudget.Description"/>).
    /// </summary>
    public string Description => _budget.Description;

    /// <summary>
    /// Parses <c>value[:count[:skip]]</c> (see the class remarks). Pure: no environment access.
    /// </summary>
    /// <param name="spec">The raw text, e.g. <c>copy</c>, <c>damaged:1</c> or <c>copy:1:1</c>; may be null or empty.</param>
    /// <returns>The plan, or null when <paramref name="spec"/> is empty or not understood (garbage is ignored).</returns>
    public static RestoreFaultPlan? Parse(string? spec)
    {
        if (!DebugFaultBudget.TryParseSpec(spec, out var name, out var budget))
            return null;

        var fault = name switch
        {
            "backup"  => InjectedRestoreFault.Backup,
            "damaged" => InjectedRestoreFault.Damaged,
            "precopy" => InjectedRestoreFault.PreCopy,
            "copy"    => InjectedRestoreFault.Copy,
            "verify"  => InjectedRestoreFault.Verify,
            "lock"    => InjectedRestoreFault.Lock,
            "rename"  => InjectedRestoreFault.Rename,
            _         => InjectedRestoreFault.None
        };

        return fault == InjectedRestoreFault.None ? null : new RestoreFaultPlan(fault, budget);
    }

    /// <summary>
    /// Reads <see cref="EnvironmentVariableName"/> and parses it — in DEBUG builds only. In Release
    /// builds the read is compiled out and this always returns null, whatever the environment holds.
    /// This is the single place the variable is read.
    /// </summary>
    /// <returns>The plan, or null when the variable is unset, not understood, or this is a Release build.</returns>
    public static RestoreFaultPlan? FromEnvironment()
    {
#if DEBUG
        return Parse(Environment.GetEnvironmentVariable(EnvironmentVariableName));
#else
        return null;
#endif
    }

    /// <summary>
    /// Asks whether the restore that has just reached <paramref name="stage"/> should fail there. Counts
    /// the call against the plan (skip first, then the failure budget) — but only when
    /// <paramref name="stage"/> is the plan's own fault; a call at any other stage neither fails nor
    /// spends the budget.
    /// </summary>
    /// <param name="stage">The stage the restore has reached, named by the fault that targets it.</param>
    /// <returns>True when the restore must fail at this stage.</returns>
    public bool TryConsume(InjectedRestoreFault stage)
        => stage != InjectedRestoreFault.None && Fault == stage && _budget.TryConsume();
}
