namespace NetworkProbe;

/// <summary>How one late-landing cycle ended.</summary>
internal enum LateOutcome
{
    /// <summary>The abandoned operation took effect on the server after the network came back.</summary>
    LandedLate,

    /// <summary>Nothing changed, and the abandoned operation was seen to end with an error (its "faulted after timeout" line).</summary>
    Died,

    /// <summary>No conclusion: the cut was not confirmed, the files were not as created, or the call was answered without the network.</summary>
    NotTested,

    /// <summary>Nothing changed and no line says the abandoned operation ended: it may still be blocked.</summary>
    StillPending,

    /// <summary>Dry run: nothing was cut, so nothing can be concluded.</summary>
    Simulated
}

/// <summary>
/// Everything recorded about one late-landing cycle (one case, one hold, one repetition): the fields of the report table
/// plus the timeline for the per-cycle log.
/// </summary>
internal sealed record LateLandingResult
{
    /// <summary>The case name (<c>late-delete</c> or <c>late-move</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Seconds the cut was held, counted from the call being issued.</summary>
    public required int HoldSeconds { get; init; }

    /// <summary>Which repetition of this case and hold this is.</summary>
    public required int Repetition { get; init; }

    /// <summary>How the cycle ended.</summary>
    public required LateOutcome Outcome { get; init; }

    /// <summary>Why, in plain words.</summary>
    public required string Reason { get; init; }

    /// <summary>Probe-clock time the call was issued (T_issue); zero when the call was never issued.</summary>
    public TimeSpan IssuedAt { get; init; }

    /// <summary>Seconds after T_issue that <c>NetworkFileOps</c> handed back its answer; null if it had not by the end of the hold.</summary>
    public TimeSpan? NetworkFileOpsReturnedAfter { get; init; }

    /// <summary>What <c>NetworkFileOps</c> returned, written out (<c>returned False</c>), or <c>(did not return)</c>.</summary>
    public string NetworkFileOpsResult { get; init; } = "-";

    /// <summary>Seconds after T_issue of the first observation that differed from the pre-cut state (an upper bound); null if none did.</summary>
    public TimeSpan? FirstChangedAfterIssue { get; init; }

    /// <summary>The state first seen changed, written out; <c>-</c> if none.</summary>
    public string ChangedState { get; init; } = "-";

    /// <summary>Seconds after T_issue of the abandoned operation's "faulted after timeout" log line; null if none was logged.</summary>
    public TimeSpan? InnerFinishedAfterIssue { get; init; }

    /// <summary>How the inner operation finished, as far as the log tells, in plain words.</summary>
    public string InnerFinishedHow { get; init; } = "-";

    /// <summary>Seconds from stopping clumsy until the share answered again; null when not measured.</summary>
    public TimeSpan? Recovery { get; init; }

    /// <summary>The case folder's last-write time on the server (server clock) before the cut; null if it could not be read.</summary>
    public DateTime? FolderMtimeBefore { get; init; }

    /// <summary>The case folder's last-write time on the server (server clock) at the last observation; null if it could not be read.</summary>
    public DateTime? FolderMtimeAfter { get; init; }

    /// <summary>How many observations were made after the share first answered.</summary>
    public int Observations { get; init; }

    /// <summary>How many of those could not look (the share did not answer in time).</summary>
    public int UnreadableObservations { get; init; }

    /// <summary>How long the observation went on after the share first answered.</summary>
    public TimeSpan ObservedFor { get; init; }

    /// <summary>The cycle's events and every line the logger received, oldest first (levels <c>EVENT</c>, <c>INFO</c>, ...).</summary>
    public IReadOnlyList<LogLine> Timeline { get; init; } = Array.Empty<LogLine>();

    /// <summary>The case name with its hold and repetition: <c>late-delete 30 s #2</c>.</summary>
    public string Label => $"{Name} {HoldSeconds} s #{Repetition}";

    /// <summary>The outcome as shown to the user.</summary>
    public string OutcomeText => Outcome switch
    {
        LateOutcome.LandedLate => "LANDED LATE",
        LateOutcome.Died => "DIED",
        LateOutcome.NotTested => "NOT TESTED",
        LateOutcome.StillPending => "STILL PENDING",
        _ => "SIMULATED"
    };
}
