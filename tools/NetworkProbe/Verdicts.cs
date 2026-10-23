using TermPoint.Services;

namespace NetworkProbe;

/// <summary>The outcome of one case.</summary>
internal enum Verdict
{
    /// <summary>The call completed within the window of the cut being confirmed, reporting the timeout or stall.</summary>
    Pass,

    /// <summary>The call waited for the network, took too long, never completed, or blocked its caller.</summary>
    Fail,

    /// <summary>No verdict could be reached (cut not confirmed, finished before the cut, a call answered without the network).</summary>
    NotTested,

    /// <summary>Dry run: the call was made with no cut, so nothing can be concluded.</summary>
    Simulated
}

/// <summary>
/// What was measured for one case, in the fields of spec section 5.6, ready to be judged.
/// </summary>
/// <param name="CallReturn">How long the call took to hand back its task; null if it never did (it blocked its caller until the end).</param>
/// <param name="CompletedAfterCut">Time from the cut being confirmed until the task completed; null if it never completed.</param>
/// <param name="CompletedBeforeCut">True when the task had already finished when the cut was confirmed.</param>
/// <param name="CompletedAfterRestore">True when the task finished only after clumsy was stopped.</param>
/// <param name="GaveUp">True when the call reported the timeout or stall.</param>
/// <param name="ErrorType">The name of the exception the call threw, or null.</param>
internal sealed record Measurement(
    TimeSpan? CallReturn,
    TimeSpan? CompletedAfterCut,
    bool CompletedBeforeCut,
    bool CompletedAfterRestore,
    bool GaveUp,
    string? ErrorType);

/// <summary>A verdict and the reason for it, in plain words.</summary>
/// <param name="Verdict">The verdict.</param>
/// <param name="Reason">Why, in one sentence.</param>
internal sealed record VerdictOutcome(Verdict Verdict, string Reason);

/// <summary>
/// Turns a <see cref="Measurement"/> into a verdict by the rules of spec section 5.6. Pure and deterministic, so the
/// rules can be read (and tested) apart from the machinery that measures.
/// </summary>
internal static class VerdictJudge
{
    /// <summary>Extra time allowed beyond the deadline before a late answer counts as a failure (spec: TimeoutMs + 2 s).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    /// <summary>A call that takes longer than this to hand back its task is flagged as blocking its caller.</summary>
    public static readonly TimeSpan CallReturnLimit = TimeSpan.FromSeconds(1);

    /// <summary>The default pass window: the deadline plus <see cref="Grace"/>.</summary>
    public static TimeSpan DefaultWindow => TimeSpan.FromMilliseconds(NetworkFileOps.TimeoutMs) + Grace;

    /// <summary>
    /// Judges one measurement.
    ///
    /// <para>Order matters: a call that finished before the cut proves nothing; then the failures (blocks its caller,
    /// never completed, completed after the network came back, too slow) are checked, because a late answer must never
    /// be mistaken for anything else; only then is a prompt answer a pass, and a prompt <i>normal</i> answer (the call
    /// was served without the network, for example from SMB's cache) is not tested.</para>
    /// </summary>
    /// <param name="m">What was measured.</param>
    /// <param name="window">How long after the cut is confirmed the call may take and still pass.</param>
    /// <returns>The verdict and its reason.</returns>
    public static VerdictOutcome Judge(Measurement m, TimeSpan window)
    {
        if (m.CompletedBeforeCut)
            return new(Verdict.NotTested, "the operation finished before the cut was confirmed");

        if (m.CallReturn is null)
            return new(Verdict.Fail, "the call blocks its caller: it had not handed back its task when the network came back");

        if (m.CallReturn > CallReturnLimit)
            return new(Verdict.Fail, $"the call blocks its caller for {m.CallReturn.Value.TotalSeconds:F1} s before handing back its task");

        if (m.CompletedAfterCut is null)
            return new(Verdict.Fail, "never completed: still not finished long after the network came back");

        var after = m.CompletedAfterCut.Value;
        if (m.CompletedAfterRestore)
            return new(Verdict.Fail, $"completed {after.TotalSeconds:F1} s after the cut, only AFTER the network came back (it waited for the network)");

        if (after > window)
            return new(Verdict.Fail, $"took {after.TotalSeconds:F1} s after the cut; the limit is {window.TotalSeconds:F1} s");

        if (m.ErrorType is not null)
            return new(Verdict.Pass, $"threw {m.ErrorType} {after.TotalSeconds:F1} s after the cut (no timeout report, but control came back in time)");

        if (m.GaveUp)
            return new(Verdict.Pass, $"gave up {after.TotalSeconds:F1} s after the cut");

        return new(Verdict.NotTested,
            $"completed normally {after.TotalSeconds:F1} s after the cut: the call was answered without the network (for example from SMB's cache), so it proves nothing");
    }
}
