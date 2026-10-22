namespace TermPoint.Tests.Harness;

/// <summary>
/// One written-down sequence of events that is replayed at the start of every generated exploration (spec item 9.3,
/// step 9.3c-1). The search is bounded, so it cannot reach every finding: F6, for instance, needs about seven events in
/// one order, beyond the exhaustive depth, and a few hundred random walks may never hit it. A known sequence is the
/// honest way to say "this route is known, replay it every time": it counts toward
/// <c>Generated_RediscoversKnownFindings</c>, and the report says which findings were found by the search, by a replay,
/// or both, so the search is never credited with something only a replay reached.
///
/// <para>A sequence may contain power-cut events (<see cref="PowerCutEvent"/>, key <c>PowerCut(Save(bob) at Save.AfterRename)</c>)
/// and pause events (<see cref="PauseEvent"/>, key <c>Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)</c>): the
/// replay builds them from the keys (<see cref="EventCatalogue.ExtrasFor"/>).</para>
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

        new("F2: an edit made while the save is running loses its marker",
            "F2",
            "Bob presses Save. His save has taken its snapshot of the working file (Save.AfterLocalSnapshot) and is held there " +
            "while Bob makes another edit. The edit is not in the snapshot, and the save's last step still deletes the " +
            "unsaved-changes marker. When Bob's computer crashes before another save lands, his kept working file has no marker and " +
            "the next start deletes it as \"nothing was edited\": the edit is gone. Found by the pause sweep (step 9.3c-2b).",
            new[]
            {
                "Open(bob)", "Pause(Save(bob) at Save.AfterLocalSnapshot; BobEdits)", "Crash(bob)"
            }),

        new("F16: a demotion still running when the user switches database",
            "F16",
            "Bob's computer wakes to find his lock was taken over, and the demotion that starts (it makes Bob's session read-only) " +
            "is held before it copies the database. Bob switches to the other database: it gets write access and the other " +
            "database's lock. The old demotion goes on against the service's current database: it copies the NEW database and " +
            "leaves Bob's session read-only while it holds the new lock (I6, both clauses). The harness runs the lost-lock handler " +
            "inline where production posts it, so the production route is the unit test CheckoutServiceTests.F16_...; this is the " +
            "same race, held deterministically (step 9.3c-2b).",
            new[]
            {
                "Open(bob)", "AdvanceTime(4m)", "Open(alice)", "Pause(Sleep(bob,10m) at Demote.AfterWorkingCopyHandled; BobSwitches)"
            }),
    };
}
