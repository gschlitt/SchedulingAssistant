using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof that three fixed findings of the data-persistence review (section 3) stay fixed. All three were fixed on 2026-10-21 (1.2.3):
/// <list type="bullet">
/// <item><description><b>F1</b>: the window's X on "Restore Unsaved Changes?" / "Can't Restore Unsaved Changes" asks the question again
/// instead of deleting the kept changes, and Discard asks "Discard Unsaved Changes?" ([Discard] / [Go Back]) before it deletes anything.</description></item>
/// <item><description><b>F12</b>: the save re-checks the lock and the shared database immediately before the rename, so a save whose
/// lock another computer took over while it uploaded is refused instead of landing over that computer's save.</description></item>
/// <item><description><b>F8</b>: the save records its fingerprint in the unsaved-changes marker before the rename, so a crash right
/// after a rename that did succeed no longer ends in "Can't Restore" when the person starts again.</description></item>
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
    /// The old F12 route with an edit of Bob's and the same pause one step later, after his D.tmp is verified (Alice's save then uses the
    /// shared D.tmp name), followed by Bob's crash and restart.
    /// </summary>
    private static readonly string[] OldF12VerifiedTmpRoute =
    {
        "Open(bob)", "Edit(bob)", "Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)", "Crash(bob)", "Open(bob)"
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
    /// F12, the other face of the route: Alice's save finishes while Bob's save is held with a verified D.tmp (she used the same D.tmp
    /// name and moved it onto D). Bob's save is refused at its final check; her edit stays in D, none of his is lost (his kept change is
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
    /// The fixed findings are off the lists: no id is a known finding, no known sequence names any, and the classifier still has its
    /// rules for them (a regression is reported as a "new" violation of that kind, not hidden). A plain unit test.
    /// </summary>
    [Fact]
    public void F1F8AndF12_AreOffTheKnownLists_SoARegressionFailsAsNew()
    {
        foreach (var id in new[] { "F1", "F8", "F12" })
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
    }
}
