using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof that fixed findings of the data-persistence review (section 3) stay fixed. F1, F8 and F12 were fixed on 2026-10-21 (1.2.3); F19 in 1.2.4 (item 4.12);
/// F2 in 1.2.4 (item 3.2); F16, F17 and F23 in 1.2.4 by the close/switch sequence (<c>designs/close-switch-sequence.md</c>):
/// <list type="bullet">
/// <item><description><b>F2</b>: an edit made while a save runs keeps the unsaved-changes marker (the save compares an edit count and SQLite's
/// <c>data_version</c> before its snapshot and at step 6), so a crash or lost lock no longer throws the edit away.</description></item>
/// <item><description><b>F16</b>: the checkout has a session id and the release waits for a running lost-lock demotion, so a demotion still running when the
/// user switches no longer lands on the new session (its pause form replays here; the production route is <c>CheckoutServiceTests.F16_...</c>).</description></item>
/// <item><description><b>F17</b>: while the checkout is ending, the final save's banners and lock loss are not delivered, so a close or switch save problem shows
/// only the "Changes Not Saved" dialog.</description></item>
/// <item><description><b>F23</b>: the post-open save runs only when the open really restored kept changes, so a Discard is not followed by a "changes are kept" banner.</description></item>
/// <item><description><b>F18</b> (also 1.2.4): a save that puts back a removed lock file shows the "lock put back" notice only if it then succeeds, so a failing
/// save shows its failure banner alone.</description></item>
/// <item><description><b>F1</b>: the window's X on "Restore Unsaved Changes?" / "Can't Restore Unsaved Changes" asks the question again
/// instead of deleting the kept changes, and Discard asks "Discard Unsaved Changes?" ([Discard] / [Go Back]) before it deletes anything.</description></item>
/// <item><description><b>F12</b>: the save re-checks the lock and the shared database immediately before the rename, so a save whose
/// lock another computer took over while it uploaded is refused instead of landing over that computer's save.</description></item>
/// <item><description><b>F8</b>: the save records its fingerprint in the unsaved-changes marker before the rename, so a crash right
/// after a rename that did succeed no longer ends in "Can't Restore" when the person starts again.</description></item>
/// <item><description><b>F19</b>: a lock-file check counts for 2 seconds, so a heartbeat held (the computer sleeps) between its check and its move onto
/// the lock file reads the lock again and does not move over a lock another computer took meanwhile.</description></item>
/// </list>
///
/// <para>The routes below are the OLD routes of these findings (for F8 and F12 the known sequences of <see cref="KnownSequences"/>, copied here
/// before the entries were removed from the list; for F1 the route the generated search used to find it). They used to END in the finding; they
/// must now show no violation at all, known or new. The classifier's rules for F1, F8 and F12 stay in <see cref="Findings"/>, and the three ids
/// are off <see cref="KnownFindings"/>, so if a route ever breaks again it is reported as a new violation and fails every generated run too. The replay tests run only with
/// <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>); the list check is a plain unit test.</para>
/// </summary>
public sealed class FixedFindingsProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public FixedFindingsProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The old F12 known sequence: Bob presses Save; his save has taken its snapshot and is held there (his computer sleeps). Alice takes
    /// over his stale lock, edits and saves. Bob's computer wakes. It used to copy its snapshot to D.tmp and rename it over the database,
    /// removing Alice's edit (I1) while the lock was hers (I4).
    /// </summary>
    private static readonly string[] OldF12KnownSequence =
    {
        "Open(bob)", "Pause(Save(bob) at Save.AfterLocalSnapshot; AliceTakesOverAndSaves)"
    };

    /// <summary>
    /// The old F12 route with an edit of Bob's and the same pause one step later, after his D.tmp is verified (Alice's save then uses a
    /// temporary file of its own; before 1.2.4 item 4.1 it was the shared D.tmp name), followed by Bob's crash and restart.
    /// </summary>
    private static readonly string[] OldF12VerifiedTmpRoute =
    {
        "Open(bob)", "Edit(bob)", "Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)", "Crash(bob)", "Open(bob)"
    };

    /// <summary>
    /// The old F19 known sequence (1.2.4 item 4.12): Bob's computer sleeps; on waking, his heartbeat has read his own lock and written its temporary file
    /// and is held before it moves that file onto the lock file. Alice takes over the (stale) lock meanwhile. It used to move Bob's renewed lock over
    /// ALICE's (invariant I9). Pause occurrence #2 is the heartbeat the harness's second tick reaches.
    /// </summary>
    private static readonly string[] OldF19KnownSequence =
    {
        "Open(bob)", "Pause(Sleep(bob,10m) at Heartbeat.AfterTmpWritten #2; AliceTakesOver)"
    };

    /// <summary>
    /// The old F16 known sequence (1.2.4, the close/switch sequence): Bob's computer wakes to find his lock taken over; the demotion that starts is
    /// held before it copies the database (<c>Demote.AfterWorkingCopyHandled</c>) while Bob switches to the other database. The harness runs the
    /// lost-lock handler inline where production posts it, so the production route is the unit test <c>CheckoutServiceTests.F16_...</c>.
    /// </summary>
    private static readonly string[] OldF16KnownSequence =
    {
        "Open(bob)", "AdvanceTime(4m)", "Open(alice)", "Pause(Sleep(bob,10m) at Demote.AfterWorkingCopyHandled; BobSwitches)"
    };

    /// <summary>The old F16 pause route: as <see cref="OldF16KnownSequence"/> with an edit of Bob's, then his crash and restart.</summary>
    private static readonly string[] OldF16PauseRoute =
    {
        "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)",
        "Pause(Sleep(bob,10m) at Demote.AfterWorkingCopyHandled; BobSwitches)", "Crash(bob)", "Open(bob)"
    };

    /// <summary>The old F17 route: Bob edits, his lock is taken over (Alice), he switches database and his final save finds the lock gone.</summary>
    private static readonly string[] OldF17Route =
    {
        "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)", "SwitchAway(bob)"
    };

    /// <summary>
    /// The old F18 route (the generated search's shrunk story): another program has the database open for reading only, Alice opens it for
    /// editing, somebody deletes the lock file, and Alice saves: the save puts the lock file back and then fails because the file is in use.
    /// </summary>
    private static readonly string[] OldF18Route =
    {
        "OtherProgramOpens(database,read)", "Open(alice)", "DeleteLockFile", "Save(alice)"
    };

    /// <summary>
    /// The old F23 route: Bob's kept changes cannot be restored because Alice took over, changed the database and left; another program has the
    /// database open when Bob switches back. The answers to the last event's dialogs are given by the test.
    /// </summary>
    private static readonly string[] OldF23Route =
    {
        "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)", "SwitchAway(bob)", "Edit(alice)", "Save(alice)", "Close(alice)",
        "OtherProgramOpens(database,read)", "SwitchBack(bob)"
    };

    /// <summary>
    /// The old F8 known sequence: Bob edits and presses Save; his computer loses power at Save.AfterRename, after the shared database has been
    /// replaced and before the unsaved-changes marker is deleted. When he starts again he is offered a restore and chooses it. It used to
    /// end in "Can't Restore" although the database already held his edit.
    /// </summary>
    private static readonly string[] OldF8KnownSequence =
    {
        "Open(bob)", "Edit(bob)", "PowerCut(Save(bob) at Save.AfterRename)", "Open(bob)"
    };

    /// <summary>
    /// The old F1 route: Bob edits and his computer crashes; when he starts again he is asked "Restore Unsaved Changes?". Answered with
    /// Discard or the window's X, the lifecycle used to delete his kept changes at once (I2 fired, classified F1).
    /// </summary>
    private static readonly string[] OldF1Route = { "Open(bob)", "Edit(bob)", "Crash(bob)", "Open(bob)" };

    /// <summary>The label of the "Restore Unsaved Changes?" dialog as the decision source sees it (computer name, then the title).</summary>
    private const string RestoreQuestion = "bob: " + HarnessTitles.RestoreUnsavedChanges;

    /// <summary>The label of the "Can't Restore Unsaved Changes" question as the decision source sees it.</summary>
    private const string CantRestoreQuestion = "bob: " + HarnessTitles.CantRestore;

    /// <summary>The label of the "Discard Unsaved Changes?" confirmation as the decision source sees it.</summary>
    private const string DiscardConfirmation = "bob: " + HarnessTitles.DiscardUnsavedConfirm;

    /// <summary>
    /// Replays the old F1 route with the given answers to the dialogs of its LAST event (Bob's restart), in the order they are asked. Every
    /// other dialog gets its simplest answer.
    /// </summary>
    /// <param name="answers">(dialog label, answer) pairs: "Affirmative", "Negative" or "X".</param>
    private static Task<RunResult> RunOldF1RouteAsync(params (string Dialog, string Answer)[] answers)
    {
        var record = new RunRecord();
        foreach (var key in OldF1Route) record.Steps.Add(new RecordedStep { EventKey = key });
        record.Steps[^1].Answers.AddRange(answers.Select(a => new RecordedAnswer(a.Dialog, a.Answer, GeneratedPrompts.ChoiceOptions)));
        return RunExecutor.ExecuteAsync(new ReplaySource(record),
            new RunOptions { Title = "a fixed-finding proof", ExtraEvents = EventCatalogue.ExtrasFor(OldF1Route) });
    }

    /// <summary>How many times the story says a computer was asked a dialog (the story writes each as: asked "title" [buttons]).</summary>
    /// <param name="story">The printed story.</param>
    /// <param name="title">The dialog's title.</param>
    private static int TimesAsked(string story, string title)
        => System.Text.RegularExpressions.Regex.Matches(story, "was asked \"" + System.Text.RegularExpressions.Regex.Escape(title) + "\"").Count;

    /// <summary>Replays the given event keys (every dialog gets its simplest answer) with the extra events they need.</summary>
    /// <param name="keys">The event keys, in order.</param>
    private static Task<RunResult> RunKeysAsync(params string[] keys)
    {
        var record = new RunRecord();
        foreach (var key in keys) record.Steps.Add(new RecordedStep { EventKey = key });
        return RunExecutor.ExecuteAsync(new ReplaySource(record),
            new RunOptions { Title = "a fixed-finding proof", ExtraEvents = EventCatalogue.ExtrasFor(keys) });
    }

    /// <summary>
    /// F12: the old known sequence ends with Bob's save REFUSED (his lock is Alice's by the time his save is about to rename) and Alice's
    /// edit still in the shared database. No violation of any kind: not I1 (her edit did not disappear), not I4 (there was no rename
    /// to see), not F12.
    /// </summary>
    [HarnessFact]
    public async Task F12_TheOldKnownSequence_EndsWithBobsSaveRefused_AndAlicesEditStillInD()
    {
        var run = await RunKeysAsync(OldF12KnownSequence);
        _output.WriteLine(run.Story);

        Assert.Equal(PauseOutcome.Interleaved, Assert.Single(run.PauseResults).Outcome);
        Assert.Empty(run.Violations);
        Assert.Contains("Alice saved the database — Success", run.Story);
        Assert.Contains("Bob saved the database — LockLost", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// F12, the other face of the route: Alice's save finishes while Bob's save is held with a verified D.tmp (she used a temporary file
    /// of her own and moved it onto D). Bob's save is refused at its final check; her edit stays in D, none of his is lost (his kept change is
    /// offered for restore later, and found not to fit the database Alice changed, which is F7, not F12), and neither I1, I2 nor I4 is reported.
    /// </summary>
    [HarnessFact]
    public async Task F12_TheOldVerifiedTmpRoute_EndsWithBobsSaveRefused_AndNothingIsLost()
    {
        var run = await RunKeysAsync(OldF12VerifiedTmpRoute);
        _output.WriteLine(run.Story);

        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F12");
        Assert.DoesNotContain(run.Violations, v => v.Invariant is "I1" or "I2" or "I4");
        Assert.Empty(run.NewViolations);
        Assert.Contains("Bob saved the database — LockLost", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// F19: the old known sequence ends with Bob's heartbeat NOT moving his lock over Alice's. The sleep makes his lock check older than two
    /// seconds, so on going on the heartbeat reads the lock again, finds Alice's name in it and leaves it alone (it asks for the ownership
    /// check instead). No violation of any kind: not I9 (nothing of Bob's is written over her lock), not F19.
    /// </summary>
    [HarnessFact]
    public async Task F19_TheOldKnownSequence_EndsWithBobsHeartbeatLeavingAlicesLockAlone()
    {
        var run = await RunKeysAsync(OldF19KnownSequence);
        _output.WriteLine(run.Story);

        Assert.Equal(PauseOutcome.Interleaved, Assert.Single(run.PauseResults).Outcome);
        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F19");
        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I9");
        Assert.Empty(run.Violations);
    }

    /// <summary>
    /// F16, the pause form (the old known sequence of <see cref="KnownSequences"/>, moved here): Bob's computer wakes to find his lock was taken
    /// over, and the demotion that starts is held before it has copied the database while Bob switches to the other database. It used to go on
    /// against the service's CURRENT database: it copied the NEW database and left Bob's session read-only while it held the new lock (I6, both
    /// clauses). Now the release waits for the running demotion (and a demotion of an ended session stops), so the switch lands cleanly.
    /// </summary>
    [HarnessFact]
    public async Task F16_TheOldKnownSequence_ADemotionHeldWhileTheUserSwitches_LandsNothingOnTheNewSession()
    {
        var run = await RunKeysAsync(OldF16KnownSequence);
        _output.WriteLine(run.Story);

        Assert.NotEmpty(run.PauseResults);
        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F16");
        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I6");
        Assert.Empty(run.Violations);
    }

    /// <summary>
    /// F16, the longer pause route of the old pause proof (Bob edits first, and after the held demotion and the switch he crashes and
    /// starts again). Both clauses of I6 used to fire; now nothing is reported, known or new.
    /// </summary>
    [HarnessFact]
    public async Task F16_TheOldPauseRoute_WithAnEditAndARestart_ShowsNoViolation()
    {
        var run = await RunKeysAsync(OldF16PauseRoute);
        _output.WriteLine(run.Story);

        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I6");
        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F16");
        Assert.Empty(run.NewViolations);
    }

    /// <summary>
    /// F17, the route the invariant walks used to declare: Bob edits, Alice takes over his stale lock, Bob switches database. His final save finds
    /// the lock gone. He used to be shown the lock-lost banner AND "Changes Not Saved" (I8 b, two messages for one problem). Now the ending save's
    /// banner is not delivered: only the "Changes Not Saved" dialog is shown, and nothing is reported (neither F17 nor the F16 these walks used to carry).
    /// </summary>
    [HarnessFact]
    public async Task F17_TheOldRoute_ASwitchWhoseFinalSaveFindsATakeover_ShowsOnlyTheDialog()
    {
        var run = await RunKeysAsync(OldF17Route);
        _output.WriteLine(run.Story);

        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F17");
        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I8");
        Assert.Empty(run.Violations);
        Assert.Contains("Changes Not Saved", run.Story);
    }

    /// <summary>
    /// F18: the old route. Alice's save put back the deleted lock file (the auto-dismissing "lock put back" notice) and then failed because
    /// another program had the database open ("couldn't save because the database file is in use"): two banners for one save (I8). Now a
    /// save shows the "lock put back" notice only if it succeeds, so this save shows its failure banner alone and nothing is reported.
    /// </summary>
    [HarnessFact]
    public async Task F18_TheOldRoute_ASaveThatPutsTheLockBackAndThenFails_ShowsOneBanner()
    {
        var run = await RunKeysAsync(OldF18Route);
        _output.WriteLine(run.Story);

        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F18");
        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I8");
        Assert.Empty(run.Violations);
    }

    /// <summary>
    /// F23, the replay of the random-walk route (1.2.4 item 8.4) as closely as the harness events allow: Bob's kept changes cannot be restored
    /// (Alice took over, changed the database and left), another program has the shared database open when Bob switches back, and Bob answers
    /// the questions with Restore, then Save a Copy (cancelling the save dialog), then Discard on "Can't Restore" when it is asked again, then Discard
    /// on the confirmation. (The other program holds the database with sharing for reads only: the database can be read, so the questions are asked, but
    /// not written, which is what made the post-open save fail. Holding it with no sharing at all makes the open fail outright, a different route.)
    /// The open used to run a post-open save that failed with a banner saying the changes are kept, although Bob had just discarded them (I5, ChangesKept).
    /// Now the post-open save runs only when the open really restored kept changes, so that banner is never shown. The route still shows F7 ("Restore?"
    /// then "Can't Restore", a separate known finding), and nothing else.
    /// </summary>
    [HarnessFact]
    public async Task F23_TheOldRoute_DiscardAfterACantRestore_WithTheDatabaseHeld_ShowsNoKeptChangesBanner()
    {
        var record = new RunRecord();
        foreach (var key in OldF23Route) record.Steps.Add(new RecordedStep { EventKey = key });
        record.Steps[^1].Answers.AddRange(new[]
        {
            new RecordedAnswer(RestoreQuestion, "Affirmative", GeneratedPrompts.ChoiceOptions),
            new RecordedAnswer(CantRestoreQuestion, "Affirmative", GeneratedPrompts.ChoiceOptions),                 // Save a Copy...
            new RecordedAnswer("bob: Save Recovered Copy", "cancel", GeneratedPrompts.PickerOptions),             // ... and cancel the save dialog
            new RecordedAnswer(CantRestoreQuestion, "Negative", GeneratedPrompts.ChoiceOptions),                    // asked again: Discard
            new RecordedAnswer(DiscardConfirmation, "Affirmative", GeneratedPrompts.ChoiceOptions)
        });
        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record),
            new RunOptions { Title = "a fixed-finding proof", ExtraEvents = EventCatalogue.ExtrasFor(OldF23Route) });
        _output.WriteLine(run.Story);

        Assert.Equal(2, TimesAsked(run.Story, HarnessTitles.CantRestore));
        Assert.Equal(1, TimesAsked(run.Story, HarnessTitles.DiscardUnsavedConfirm));
        Assert.DoesNotContain(run.Violations, v => v.FindingId == "F23");
        Assert.Empty(run.NewViolations);

        // The only thing the route still shows is F7 (the restore question is asked and then refused: a separate, known finding, not about
        // the "changes are kept" promise), so no breach of invariant I5 other than F7 and no "kept" promise broken.
        Assert.All(run.Violations, v => Assert.Equal("F7", v.FindingId));
    }

    /// <summary>
    /// F8: the old known sequence ends with the Restore SUCCEEDING. The power cut at Save.AfterRename really fired, the restart is offered
    /// "Restore Unsaved Changes?" and nothing else is asked ("Can't Restore" never appears), Bob writes again with his edit in his session,
    /// and that edit is in the shared database. No violation of any kind.
    /// </summary>
    [HarnessFact]
    public async Task F8_TheOldKnownSequence_EndsWithTheRestoreSucceeding()
    {
        var run = await RunKeysAsync(OldF8KnownSequence);
        _output.WriteLine(run.Story);

        Assert.Equal(new[] { "bob@Save.AfterRename" }, run.FiredCuts);
        Assert.Empty(run.Violations);
        Assert.Contains("Restore Unsaved Changes?", run.Story);
        Assert.DoesNotContain("Can't Restore", run.Story);
        Assert.Contains("bob-1: the shared database; bob's live session", run.Story);
    }

    /// <summary>
    /// F1, Discard then Discard: the person chooses Discard on "Restore Unsaved Changes?", is asked "Discard Unsaved Changes?" and chooses
    /// Discard. The kept changes ARE deleted (bob-1 is nowhere), and that is not a violation: the confirmation is the person's decision, so
    /// invariant I2 excuses the edit. Before the fix the first Discard deleted them with nothing in between.
    /// </summary>
    [HarnessFact]
    public async Task F1_TheOldRoute_DiscardThenDiscard_DeletesTheChangesAsAConfirmedDiscard()
    {
        var run = await RunOldF1RouteAsync((RestoreQuestion, "Negative"), (DiscardConfirmation, "Affirmative"));
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Equal(1, TimesAsked(run.Story, HarnessTitles.RestoreUnsavedChanges));
        Assert.Equal(1, TimesAsked(run.Story, HarnessTitles.DiscardUnsavedConfirm));
        Assert.Matches("bob-1: nowhere", run.Story);
    }

    /// <summary>
    /// F1, the X: the person closes "Restore Unsaved Changes?" with the X. It is not a decision, so the question is asked again; the person
    /// then chooses Restore and bob-1 is back in Bob's session. No violation. Before the fix the X deleted the changes.
    /// </summary>
    [HarnessFact]
    public async Task F1_TheOldRoute_TheX_AsksAgain_ThenRestoreBringsTheEditBack()
    {
        var run = await RunOldF1RouteAsync((RestoreQuestion, "X"), (RestoreQuestion, "Affirmative"));
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Equal(2, TimesAsked(run.Story, HarnessTitles.RestoreUnsavedChanges));
        Assert.Contains("closed it with the X", run.Story);
        Assert.DoesNotContain(HarnessTitles.DiscardUnsavedConfirm, run.Story);
        Assert.Matches("bob-1: .*bob's live session", run.Story);
    }

    /// <summary>
    /// F1, Discard then Go Back: the person chooses Discard, is asked "Discard Unsaved Changes?" and chooses Go Back; the original question
    /// is asked again and the person chooses Restore. bob-1 is back in Bob's session. No violation.
    /// </summary>
    [HarnessFact]
    public async Task F1_TheOldRoute_DiscardThenGoBack_AsksTheQuestionAgain_ThenRestoreBringsTheEditBack()
    {
        var run = await RunOldF1RouteAsync(
            (RestoreQuestion, "Negative"), (DiscardConfirmation, "Negative"), (RestoreQuestion, "Affirmative"));
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Equal(2, TimesAsked(run.Story, HarnessTitles.RestoreUnsavedChanges));
        Assert.Equal(1, TimesAsked(run.Story, HarnessTitles.DiscardUnsavedConfirm));
        Assert.Contains("chose \"Go Back\"", run.Story);
        Assert.Matches("bob-1: .*bob's live session", run.Story);
    }

    /// <summary>
    /// F1, the confirmation's own X counts as Go Back: Discard, then the X on "Discard Unsaved Changes?", then the original question again
    /// and Restore. bob-1 is back in Bob's session. No violation.
    /// </summary>
    [HarnessFact]
    public async Task F1_TheOldRoute_TheXOnTheConfirmation_IsGoBack()
    {
        var run = await RunOldF1RouteAsync(
            (RestoreQuestion, "Negative"), (DiscardConfirmation, "X"), (RestoreQuestion, "Affirmative"));
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Equal(2, TimesAsked(run.Story, HarnessTitles.RestoreUnsavedChanges));
        Assert.Equal(1, TimesAsked(run.Story, HarnessTitles.DiscardUnsavedConfirm));
        Assert.Matches("bob-1: .*bob's live session", run.Story);
    }

    /// <summary>
    /// A decision source that always picks the most obstructive answer on offer: the X when it is offered, otherwise the second button
    /// (Discard on a question, Go Back on the confirmation), otherwise the first. It replays the old F1 route's events. Without a bound it
    /// would send the recovery questions back for ever.
    /// </summary>
    private sealed class ObstinateSource : IDecisionSource
    {
        /// <summary>Replays the route's events (its answers are never used).</summary>
        private readonly ReplaySource _events;

        /// <summary>Creates the source for the old F1 route.</summary>
        public ObstinateSource()
        {
            var record = new RunRecord();
            foreach (var key in OldF1Route) record.Steps.Add(new RecordedStep { EventKey = key });
            _events = new ReplaySource(record);
        }

        /// <inheritdoc/>
        public string? NextEvent(IReadOnlyList<EventOption> enabled, int eventsSoFar) => _events.NextEvent(enabled, eventsSoFar);

        /// <inheritdoc/>
        public string Answer(string dialog, IReadOnlyList<string> options)
            => options.Contains("X") ? "X" : options.Contains("Negative") ? "Negative" : options[0];

        /// <inheritdoc/>
        public bool AfterEvent(Func<string> stateKey, int eventsSoFar) => true;
    }

    /// <summary>
    /// The harness cannot loop for ever on the recovery questions. A person who answers the X whenever it is offered, and Discard / Go Back
    /// otherwise, sends the question back only <see cref="RunContext.MaxRecoveryReasksPerEvent"/> time(s): the question then stops offering
    /// the X and the confirmation offers only Discard. Here: "Restore?" is closed with the X (one re-ask, the bound is used up), asked again,
    /// Discard is chosen, and the confirmation offers only Discard, which ends the loop as a confirmed discard. No violation.
    /// </summary>
    [HarnessFact]
    public async Task TheRecoveryQuestions_CannotBeSentBackForEver_EvenByAPersonWhoAlwaysObstructs()
    {
        var run = await RunExecutor.ExecuteAsync(new ObstinateSource(),
            new RunOptions { Title = "an obstinate person", ExtraEvents = EventCatalogue.ExtrasFor(OldF1Route) });
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Equal(2, TimesAsked(run.Story, HarnessTitles.RestoreUnsavedChanges));
        Assert.Equal(1, TimesAsked(run.Story, HarnessTitles.DiscardUnsavedConfirm));
        Assert.Matches("bob-1: nowhere", run.Story);
    }

    /// <summary>
    /// The classifier still has its F1 rule, as the regression detector: a lost edit in a step in which a recovery question was answered with
    /// the second button but NOT followed by "Discard Unsaved Changes?", or with the X but NOT followed by the same question again, is F1 (and
    /// F1 is not a known finding, so it fails every generated run as new). The same answers WITH their follow-up are not F1. A plain unit test.
    /// </summary>
    [Fact]
    public void F1_TheClassifierStillNamesAnUnconfirmedDiscard_ButNotAConfirmedOne()
    {
        static PromptCall Asked(string title, ChoiceAnswer answer) => new(PromptKind.Choice, title, "text", "A|B", answer);
        static string Classify(params PromptCall[] prompts) => Findings.Classify(new Violation
        {
            Step = 1, Invariant = "I2", Kind = ViolationKind.EditLost, Key = "k", Computer = "bob", Edit = "bob-1",
            Reason = "r", ComputerPrompts = prompts
        });

        var restore = HarnessTitles.RestoreUnsavedChanges;
        var cantRestore = HarnessTitles.CantRestore;
        var confirm = HarnessTitles.DiscardUnsavedConfirm;

        // The old behaviour: acted on at once.
        Assert.Equal("F1", Classify(Asked(restore, ChoiceAnswer.ClosedWithX)));
        Assert.Equal("F1", Classify(Asked(restore, ChoiceAnswer.Negative)));
        Assert.Equal("F1", Classify(Asked(cantRestore, ChoiceAnswer.Negative)));
        Assert.False(KnownFindings.IsKnown("F1"));

        // The fixed behaviour: followed up, so a loss in that step is not the F1 bug.
        Assert.NotEqual("F1", Classify(Asked(restore, ChoiceAnswer.ClosedWithX), Asked(restore, ChoiceAnswer.Affirmative)));
        Assert.NotEqual("F1", Classify(Asked(restore, ChoiceAnswer.Negative), Asked(confirm, ChoiceAnswer.Affirmative)));
        Assert.NotEqual("F1", Classify(
            Asked(cantRestore, ChoiceAnswer.Negative), Asked(confirm, ChoiceAnswer.ClosedWithX), Asked(cantRestore, ChoiceAnswer.Affirmative)));
    }

    /// <summary>
    /// The old F2 known sequence (1.2.4 item 3.2): Bob presses Save; his save has taken its snapshot (<c>Save.AfterLocalSnapshot</c>) and is held
    /// there while Bob makes another edit. The edit is not in the snapshot. The save's last step used to delete the unsaved-changes marker, so when
    /// Bob's computer crashed before another save landed, the next start deleted his unmarked working file as "nothing was edited".
    /// </summary>
    private static readonly string[] OldF2KnownSequence =
    {
        "Open(bob)", "Pause(Save(bob) at Save.AfterLocalSnapshot; BobEdits)", "Crash(bob)"
    };

    /// <summary>
    /// F2: the old route (the edit made during the save, then the crash, then Bob starts again). The save now notices the edit (the edit count and
    /// SQLite's data_version moved since its snapshot) and KEEPS the marker, so the next start offers the edit back ("Restore Unsaved Changes?")
    /// instead of deleting it: no violation at all, known or new, and the pause really interleaved.
    /// </summary>
    [HarnessFact]
    public async Task F2_AnEditDuringTheOwnSave_KeepsItsMarker_AndIsOfferedBackAfterACrash()
    {
        var run = await RunKeysAsync(OldF2KnownSequence.Append("Open(bob)").ToArray());
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Empty(run.NewViolations);
        Assert.Equal(PauseOutcome.Interleaved, Assert.Single(run.PauseResults).Outcome);
        Assert.Contains("Restore Unsaved Changes?", run.Story);
    }

    /// <summary>
    /// The fixed findings are off the lists: no id is a known finding, no known sequence names any, and the classifier still has its
    /// rules for them (a regression is reported as a "new" violation of that kind, not hidden). A plain unit test.
    /// </summary>
    [Fact]
    public void TheFixedFindings_AreOffTheKnownLists_SoARegressionFailsAsNew()
    {
        foreach (var id in new[] { "F1", "F2", "F8", "F12", "F16", "F17", "F18", "F19", "F23" })
        {
            Assert.False(KnownFindings.IsKnown(id), $"{id} is fixed and must not be a known finding");
            Assert.DoesNotContain(id, KnownFindings.Ids);
            Assert.DoesNotContain(KnownSequences.All, s => s.Finding == id);
        }

        // The classifier still names them, and a violation it names is "not known", i.e. new.
        var f12 = new Violation
        {
            Step = 1, Invariant = "I4", Kind = ViolationKind.WriteWithoutLockOrOnStaleVersion, Key = "k", Computer = "bob",
            CrashPoint = "Save.AfterFinalCheck", Reason = "r"
        };
        Assert.Equal("F12", Findings.Classify(f12));
        Assert.False(KnownFindings.IsKnown(Findings.Classify(f12)));

        var f19 = new Violation
        {
            Step = 1, Invariant = "I9", Kind = ViolationKind.LockFileOfAnotherSessionWritten, Key = "k", Computer = "bob",
            Operation = "File.Move", Reason = "r"
        };
        Assert.Equal("F19", Findings.Classify(f19));
        Assert.False(KnownFindings.IsKnown(Findings.Classify(f19)));
    }
}
