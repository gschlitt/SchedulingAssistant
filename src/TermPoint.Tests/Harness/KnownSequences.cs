namespace TermPoint.Tests.Harness;

/// <summary>
/// One written-down sequence of events that is replayed at the start of every generated exploration (spec item 9.3,
/// step 9.3c-1). The search is bounded, so it cannot reach every finding: F6, for instance, needs about seven events in
/// one order, beyond the exhaustive depth, and a few hundred random walks may never hit it. A known sequence is the
/// honest way to say "this route is known, replay it every time": it counts toward
/// <c>Generated_RediscoversKnownFindings</c>, and the report says which findings were found by the search, by a replay,
/// or both, so the search is never credited with something only a replay reached.
///
/// <para>A sequence names events only (keys as <see cref="HarnessEvent.Key"/> writes them). Every dialog is answered
/// with its simplest option, exactly as <see cref="ReplaySource"/> does for a recorded run that has no answers: take
/// over, wait to completion, Restore, Save a Copy…. An event that is not enabled when its turn comes is skipped, so a
/// sequence that stops being possible shows up as the finding no longer being reproduced.</para>
/// </summary>
/// <param name="Name">A short name for the report ("F6: judged at the next open of the same database").</param>
/// <param name="Finding">The known finding the replay must show ("F6"). A replay that no longer shows it fails its test.</param>
/// <param name="Reason">One line: why this sequence is on the list.</param>
/// <param name="Events">The event keys, in order.</param>
internal sealed record KnownSequence(string Name, string Finding, string Reason, IReadOnlyList<string> Events)
{
    /// <summary>The sequence as a record a <see cref="ReplaySource"/> can replay.</summary>
    public RunRecord ToRecord()
    {
        var record = new RunRecord();
        foreach (var key in Events) record.Steps.Add(new RecordedStep { EventKey = key });
        return record;
    }
}

/// <summary>
/// The list of <see cref="KnownSequence"/>s. To add one, add an entry to <see cref="All"/>: a name, the finding it shows,
/// one line of reason and the event keys. When a finding is fixed and taken off <see cref="KnownFindings"/>, remove the
/// sequences that showed it (their replay then fails as a new violation, which is the proof the fix worked).
/// </summary>
internal static class KnownSequences
{
    /// <summary>Every known sequence, in the order they are replayed.</summary>
    public static IReadOnlyList<KnownSequence> All { get; } = new KnownSequence[]
    {
        new("F6: judged at the next open of the same database",
            "F6",
            "Bob's stale lock is taken over, he switches away; the promise (a restore will be offered next time he opens " +
            "dept.db) is judged when he comes back to dept.db, where Restore is chosen and \"Can't Restore\" follows. About " +
            "seven events in one order: beyond the exhaustive depth, and not hit by the random walks at the default budget.",
            new[]
            {
                "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)", "SwitchAway(bob)", "Close(alice)", "SwitchBack(bob)"
            }),
    };
}
