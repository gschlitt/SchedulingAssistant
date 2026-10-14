using TermPoint.Exceptions;

namespace TermPoint.Services;

/// <summary>
/// One run of the open-time integrity check of the local copy, as <see cref="LocalCopyIntegrity"/> sees it.
/// </summary>
/// <param name="Completed">
/// False when the check did not finish within its deadline (an unreachable location): there is no verdict, and
/// the open carries on so the checkout's own handling can report the unreachable location.
/// </param>
/// <param name="Result">The verdict; meaningless unless <paramref name="Completed"/>.</param>
/// <param name="Summary">SQLite's short account of why the copy did not pass; null when it passed.</param>
/// <param name="Evidence">
/// What lay behind a Damaged verdict (<see cref="DamageEvidence"/>); decides whether the damage is reported (spec item
/// 5, step 5e). <see cref="DamageEvidence.None"/> for every other verdict.
/// </param>
/// <param name="Injected">
/// True when the verdict was made up by the DEBUG fault plan (<see cref="IntegrityFaultPlan"/>). Fake damage is acted
/// on like real damage, but is never reported to BugSnag.
/// </param>
public readonly record struct LocalCopyCheck(
    bool Completed,
    IntegrityCheckResult Result,
    string? Summary,
    DamageEvidence Evidence = DamageEvidence.None,
    bool Injected = false);

/// <summary>
/// The open-time integrity check of the local copy — D' for a writer, D'' for a reader — and the re-check of the
/// shared database that follows a failure (spec item 5, step 5d). Pulled out of <c>App.InitializeServices</c> so
/// the three-way verdict and its retry count can be unit-tested without building a DI container.
///
/// <para><b>Three-way.</b> A copy that another program holds open (antivirus checking a file that was just copied)
/// used to count as damaged, because the check was a yes/no. It is now Ok / Locked / Damaged
/// (<see cref="IntegrityCheckResult"/>): a locked copy is retried quietly (<see cref="MaxAttempts"/> attempts in
/// all, <see cref="RetryDelay"/> apart) and, if it stays locked, reported as <c>DatabaseInUseException</c>; only
/// positive evidence of damage is <c>DatabaseCorruptException</c>.</para>
/// </summary>
public static class LocalCopyIntegrity
{
    /// <summary>How many times the check is attempted, in all, before a copy that stays unreadable is reported as in use (decision, 2026-10-14).</summary>
    public const int MaxAttempts = 3;

    /// <summary>How long to wait between two attempts (decision, 2026-10-14).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Runs the check once: the DEBUG-ONLY fault plan first (<see cref="InjectedIntegrityFault.Locked"/> /
    /// <see cref="InjectedIntegrityFault.Damaged"/> — each call counts as one; a <see cref="InjectedIntegrityFault.Shared"/>
    /// plan also reports damage here, without spending anything, while it has failures left), then the real, deadline-bounded
    /// <see cref="NetworkFileOps.CheckIntegrityWithSummaryAsync"/>. Blocks the calling thread, which is safe and
    /// bounded: <c>NetworkFileOps</c> chains <c>ConfigureAwait(false)</c> throughout, so nothing re-enters the
    /// dispatcher, and the wait caps at the deadline.
    /// </summary>
    /// <param name="path">Full path of the local copy (D' or D'').</param>
    /// <param name="faults">The DEBUG-ONLY plan, or null (always null in a Release build).</param>
    /// <param name="logger">Receives the line announcing an injected fault.</param>
    /// <returns>The check; never throws (an unexpected failure to examine the file is an Unreadable verdict).</returns>
    public static LocalCopyCheck CheckOnce(string path, IntegrityFaultPlan? faults, IAppLogger logger)
    {
        if (faults is not null)
        {
            if (faults.TryConsume(InjectedIntegrityFault.Locked))
            {
                logger.LogInfo(
                    $"[DEBUG fault injection] {IntegrityFaultPlan.EnvironmentVariableName}: the open-time check of the " +
                    $"local copy reports it as locked; {faults.Description}.");
                return new LocalCopyCheck(true, IntegrityCheckResult.Unreadable, "DEBUG fault injection: locked", Injected: true);
            }

            if (faults.TryConsume(InjectedIntegrityFault.Damaged))
            {
                logger.LogInfo(
                    $"[DEBUG fault injection] {IntegrityFaultPlan.EnvironmentVariableName}: the open-time check of the " +
                    $"local copy reports it as damaged; {faults.Description}.");
                return new LocalCopyCheck(true, IntegrityCheckResult.Damaged, "DEBUG fault injection: damaged",
                    DamageEvidence.IntegrityProblems, Injected: true);
            }

            // The "shared database is damaged" story: the copy made from it fails its check too, until the
            // handler's re-check (which spends the budget) has been reached. Peeks; spends nothing.
            if (faults.Fault == InjectedIntegrityFault.Shared && faults.HasFailuresLeft)
            {
                logger.LogInfo(
                    $"[DEBUG fault injection] {IntegrityFaultPlan.EnvironmentVariableName}: the open-time check of the " +
                    "local copy reports it as damaged (the shared database is meant to be damaged too).");
                return new LocalCopyCheck(true, IntegrityCheckResult.Damaged, "DEBUG fault injection: shared database damaged",
                    DamageEvidence.IntegrityProblems, Injected: true);
            }
        }

        var (completed, check) = NetworkFileOps.CheckIntegrityWithSummaryAsync(path).GetAwaiter().GetResult();
        return new LocalCopyCheck(completed, check.Result, check.Summary, check.Evidence);
    }

    /// <summary>
    /// The three-way gate: checks the local copy at <paramref name="path"/> and returns only when it may be opened.
    /// <list type="bullet">
    /// <item><description><b>Ok</b> → returns.</description></item>
    /// <item><description><b>Did not complete</b> (the location is unreachable) → logs and returns: the checkout that
    /// follows has its own deadlines and surfaces the standard "network unreachable" message, and a verdict on a file
    /// that cannot be read means nothing anyway.</description></item>
    /// <item><description><b>Unreadable</b> (locked by another program, access denied, an I/O error) → waits
    /// <paramref name="retryDelay"/> and tries again, up to <paramref name="maxAttempts"/> attempts in all; if the
    /// last attempt is unreadable too, throws <c>DatabaseInUseException</c>. Each attempt is quiet: no dialog, no
    /// banner, no report.</description></item>
    /// <item><description><b>Damaged</b> → reports positive evidence of damage to the developers
    /// (<see cref="DatabaseDamageReporter"/>, kind <see cref="DamagedFileKind.LocalCopy"/>; never for an injected DEBUG
    /// fault), then throws <c>DatabaseCorruptException</c> at once, carrying SQLite's summary. Damage is never retried:
    /// a damaged file does not heal by waiting.</description></item>
    /// </list>
    /// </summary>
    /// <param name="path">Full path of the local copy (D' or D''), which must exist.</param>
    /// <param name="check">Runs the check once for a path (production: <see cref="CheckOnce"/>).</param>
    /// <param name="logger">Receives one information line per unreadable attempt and per verdict.</param>
    /// <param name="sleep">Waits between attempts; defaults to <see cref="Thread.Sleep(TimeSpan)"/>. Tests pass a recorder so no time is spent.</param>
    /// <param name="maxAttempts">How many attempts in all; defaults to <see cref="MaxAttempts"/>.</param>
    /// <param name="retryDelay">The wait between attempts; defaults to <see cref="RetryDelay"/>.</param>
    /// <exception cref="DatabaseInUseException">The copy was unreadable on every attempt.</exception>
    /// <exception cref="DatabaseCorruptException">The copy shows positive evidence of damage.</exception>
    public static void VerifyAtOpen(
        string path,
        Func<string, LocalCopyCheck> check,
        IAppLogger logger,
        Action<TimeSpan>? sleep = null,
        int maxAttempts = MaxAttempts,
        TimeSpan? retryDelay = null)
    {
        sleep ??= Thread.Sleep;
        var delay = retryDelay ?? RetryDelay;
        string? lastSummary = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var run = check(path);

            if (!run.Completed)
            {
                logger.LogInfo("LocalCopyIntegrity: the open-time integrity check timed out — skipped (location unreachable); checkout will surface it.");
                return;
            }

            switch (run.Result)
            {
                case IntegrityCheckResult.Ok:
                    return;

                case IntegrityCheckResult.Damaged:
                    logger.LogInfo($"LocalCopyIntegrity: the local copy failed its integrity check ({run.Summary}).");
                    // Spec item 5, step 5e: positive evidence of damage inside the copy goes to the developers (once per
                    // run). A fault injected by the DEBUG plan never does. Never throws, and no path is sent.
                    DatabaseDamageReporter.ReportIfDamaged(
                        DamagedFileKind.LocalCopy, run.Evidence, run.Summary, logger, injected: run.Injected);
                    throw new DatabaseCorruptException(path, run.Summary);
            }

            // Unreadable: another program has it open (or access is denied). Wait and look again.
            lastSummary = run.Summary;
            logger.LogInfo(
                $"LocalCopyIntegrity: the local copy could not be examined, attempt {attempt} of {maxAttempts} ({run.Summary}).");
            if (attempt < maxAttempts)
                sleep(delay);
        }

        throw new DatabaseInUseException(path, maxAttempts, lastSummary);
    }

    /// <summary>
    /// Re-checks the SHARED database D after its local copy failed (the read-only front-door check,
    /// <see cref="DatabaseValidator.ValidateAsync"/>), so the handler can tell "the database is damaged" from "only
    /// this computer's copy is". The DEBUG-ONLY <see cref="InjectedIntegrityFault.Shared"/> fault makes it report
    /// damage.
    /// </summary>
    /// <param name="sharedPath">Full path of D.</param>
    /// <param name="faults">The DEBUG-ONLY plan, or null (always null in a Release build).</param>
    /// <param name="logger">Receives the line announcing an injected fault.</param>
    /// <returns>The validation result (deadline-bounded; an unreachable location is Unreachable).</returns>
    public static async Task<DatabaseValidationResult> RecheckSharedDatabaseAsync(
        string sharedPath, IntegrityFaultPlan? faults, IAppLogger logger)
    {
        if (faults is not null && faults.TryConsume(InjectedIntegrityFault.Shared))
        {
            logger.LogInfo(
                $"[DEBUG fault injection] {IntegrityFaultPlan.EnvironmentVariableName}: the re-check of the shared " +
                $"database reports it as damaged; {faults.Description}. (It is not a real check, so nothing is reported.)");
            return DatabaseValidationResult.Corrupt;
        }

        // The real check: the front-door validator reports positive evidence of damage as SharedDatabase (step 5e).
        return await DatabaseValidator.ValidateAsync(sharedPath, logger: logger);
    }

    /// <summary>
    /// Maps the front-door validator's result to the decision table's input.
    /// </summary>
    /// <param name="validation">The result of <see cref="DatabaseValidator.ValidateAsync"/>.</param>
    /// <returns>The matching verdict; a result added later maps to Unreadable (the retryable wording, never "damaged").</returns>
    public static SharedDatabaseVerdict ToVerdict(DatabaseValidationResult validation) => validation switch
    {
        DatabaseValidationResult.Ok          => SharedDatabaseVerdict.Ok,
        DatabaseValidationResult.Corrupt     => SharedDatabaseVerdict.Damaged,
        DatabaseValidationResult.Unreachable => SharedDatabaseVerdict.Unreachable,
        DatabaseValidationResult.Missing     => SharedDatabaseVerdict.Missing,
        _                                    => SharedDatabaseVerdict.Unreadable
    };

    /// <summary>
    /// Turns the result of the one fresh copy (<see cref="CheckoutService.RefreshLocalCopyAfterFailedCheckAsync"/>)
    /// into the decision table's inputs. Only a fresh copy that was made and still failed, or one that could not be
    /// made for a reason on this computer, is "keeps failing". A copy that could not be made because the shared
    /// database's location stopped responding, or because a file was held by another program, is reported the way
    /// the front door reports those cases — telling the user "this computer has a problem" would be wrong then.
    /// </summary>
    /// <param name="result">What the fresh-copy attempt reported.</param>
    /// <param name="shared">The shared database's verdict so far (normally <see cref="SharedDatabaseVerdict.Ok"/>).</param>
    /// <returns>
    /// The verdict and fresh-copy state to ask <see cref="LocalCopyCheckDecision.Decide"/> with next:
    /// Ok → (unchanged, Made) so the open is retried; NowReadOnly → likewise (unchanged, Made): the fresh copy was
    /// made and then, when the version gate ran again on it, the session became a read-only one on a fresh snapshot,
    /// which is opened the same way (from <see cref="CheckoutService.WorkingPath"/>), so it is "open again" too;
    /// Unreachable / Unreadable → (that verdict, NotTried) so the table shows the matching front-door message
    /// (this includes a re-run gate that could not read or raise the stamp); anything else → (unchanged, Failed) so
    /// it shows "keeps failing".
    /// </returns>
    public static (SharedDatabaseVerdict Shared, FreshCopyState Fresh) AfterFreshCopy(
        LocalCopyRefreshResult result, SharedDatabaseVerdict shared) => result switch
    {
        LocalCopyRefreshResult.Ok          => (shared, FreshCopyState.Made),
        LocalCopyRefreshResult.NowReadOnly => (shared, FreshCopyState.Made),
        LocalCopyRefreshResult.Unreachable => (SharedDatabaseVerdict.Unreachable, FreshCopyState.NotTried),
        LocalCopyRefreshResult.Unreadable  => (SharedDatabaseVerdict.Unreadable, FreshCopyState.NotTried),
        _                                  => (shared, FreshCopyState.Failed)
    };
}
