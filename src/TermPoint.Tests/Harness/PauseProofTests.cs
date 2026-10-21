using System.Diagnostics;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for the pause (spec item 9.3, step 9.3c-2b): an operation held at a named crash point while one other event happens,
/// then released. Like the other proof tests they make the thing really happen and assert what the harness sees: that the operation
/// is really held and really released, that a case replays to the same story and shrinks, that a deadlock becomes a Hang and not a
/// hung test run, that designed waiting is not a violation, and that the F2, F12 and F16 routes (and invariant I6) classify as the
/// design note says. They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>), except the plain key test.
/// </summary>
public sealed class PauseProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public PauseProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>Shorter limits for the tests that plant a stuck pause: they must end in a few seconds, not in half a minute.</summary>
    private static readonly PauseLimits QuickLimits = new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

    /// <summary>Replays the given event keys (every dialog gets its simplest answer) with the extra events they need.</summary>
    /// <param name="keys">The event keys, in order.</param>
    private static Task<RunResult> RunKeysAsync(params string[] keys)
    {
        var record = new RunRecord();
        foreach (var key in keys) record.Steps.Add(new RecordedStep { EventKey = key });
        return RunExecutor.ExecuteAsync(new ReplaySource(record),
            new RunOptions { Title = "a pause proof", ExtraEvents = EventCatalogue.ExtrasFor(keys) });
    }

    /// <summary>The keys of the F2 route: Bob edits while his own save is held after its snapshot, then his computer crashes.</summary>
    private static readonly string[] F2Route =
    {
        "Open(bob)", "Edit(bob)", "Pause(Save(bob) at Save.AfterLocalSnapshot; BobEdits)", "Crash(bob)", "Open(bob)"
    };

    /// <summary>The keys of the F12 route: Alice takes over and saves while Bob's save is held before it has copied to D.tmp; his rename then lands over her save.</summary>
    private static readonly string[] F12Route =
    {
        "Open(bob)", "Edit(bob)", "Pause(Save(bob) at Save.AfterLocalSnapshot; AliceTakesOverAndSaves)", "Crash(bob)", "Open(bob)"
    };

    /// <summary>The keys of the F16 route: Bob's computer wakes to find his lock gone; the demotion that starts is held while Bob switches database.</summary>
    private static readonly string[] F16Route =
    {
        "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)",
        "Pause(Sleep(bob,10m) at Demote.AfterWorkingCopyHandled; BobSwitches)", "Crash(bob)", "Open(bob)"
    };

    // ═════════════════════════════════════════════════════════════════════════
    // The pause itself
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The operation is REALLY held while the event runs, and really released afterwards. Bob's save is held at
    /// <c>Save.AfterTmpVerify</c>; the event looks at the share: D.tmp is there (the save got that far), D has not changed (the
    /// rename has not happened) and Bob's save has not returned. After the pause the save has finished: D holds the edit and D.tmp
    /// is gone. The story shows the hold before the event's lines.
    /// </summary>
    [HarnessFact]
    public async Task Pause_HoldsTheOperationWhileTheEventRuns_AndReleasesIt()
    {
        using var world = new World(_output, nameof(Pause_HoldsTheOperationWhileTheEventRuns_AndReleasesIt));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        var tmp = world.DatabasePath + ".tmp";
        List<string>? editsDuring = null;
        bool tmpDuring = false, savedDuring = true;
        Task<SaveOutcome>? save = null;

        var result = await world.RunWithPauseAsync(bob, "Save.AfterTmpVerify", 1, "Bob presses Save",
            operation: () => save = bob.SaveAsync(),
            eventStory: "somebody looks", eventIsPossible: () => true,
            pauseEvent: () =>
            {
                editsDuring = world.EditsInDatabase();
                tmpDuring   = File.Exists(tmp);
                savedDuring = save is { IsCompleted: true };
                return Task.CompletedTask;
            });

        Assert.Equal(PauseOutcome.Interleaved, result.Outcome);
        Assert.Empty(editsDuring!);      // the rename had not happened
        Assert.True(tmpDuring);          // but the save had got as far as a verified D.tmp
        Assert.False(savedDuring);       // and was still held
        Assert.Equal(SaveOutcome.Success, await save!);
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        Assert.False(File.Exists(tmp));

        var story = world.PrintStory(Array.Empty<string>());
        Assert.True(story.IndexOf("is held there", StringComparison.Ordinal) > 0);
        world.Finish();
    }

    /// <summary>
    /// A pause case is an ordinary decision list: replayed from its recorded decisions it gives the same decisions, the same
    /// story and the same findings, which is what shrinking and "run it again" rely on. Uses the F12 route, whose pause makes Alice
    /// ask two dialog questions during the pause.
    /// </summary>
    [HarnessFact]
    public async Task PauseCase_ReplaysToTheSameStory()
    {
        var first  = await RunKeysAsync(F12Route);
        var record = first.Record;
        var second = await RunExecutor.ExecuteAsync(new ReplaySource(record),
            new RunOptions { Title = "a pause proof", ExtraEvents = EventCatalogue.ExtrasFor(record.Steps.Select(s => s.EventKey)) });
        _output.WriteLine(first.Story);

        Assert.Contains("Bob's operation reached Save.AfterLocalSnapshot and is held there", first.Story);
        Assert.Equal(first.Record.ToLines(), second.Record.ToLines());
        Assert.Equal(first.Story, second.Story);
        Assert.Equal(first.Violations.Select(v => v.Signature), second.Violations.Select(v => v.Signature));
        Assert.Equal(PauseOutcome.Interleaved, Assert.Single(first.PauseResults).Outcome);
        Assert.Empty(first.NewViolations);
    }

    /// <summary>
    /// The existing shrinker shortens a long pause case: Bob opens, edits, saves, edits again, and in his NEXT save, held after the
    /// snapshot, edits once more, then crashes: the loss (F2) is shrunk to the few events that matter, and the pause stays in it.
    /// </summary>
    [HarnessFact]
    public async Task PauseCase_IsShrunkByTheExistingShrinker()
    {
        var keys = new[]
        {
            "Open(bob)", "Edit(bob)", "Save(bob)", "Edit(bob)", "AdvanceTime(30s)", "Edit(bob)",
            "Pause(Save(bob) at Save.AfterLocalSnapshot; BobEdits)", "Crash(bob)", "Open(bob)"
        };
        var options = new RunOptions { Title = "a long pause case", ExtraEvents = EventCatalogue.ExtrasFor(keys) };
        var record  = new RunRecord();
        foreach (var key in keys) record.Steps.Add(new RecordedStep { EventKey = key });

        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record), options);
        var f2  = run.Violations.FirstOrDefault(v => v.FindingId == "F2");
        Assert.NotNull(f2);

        var shrunk = await Shrinker.ShrinkAsync(run.Record.Take(f2!.EventIndex), f2.Signature, options);
        _output.WriteLine(shrunk.Story);

        Assert.True(shrunk.Reproduced);
        Assert.True(shrunk.Events < keys.Length, $"the case was not shortened: {shrunk.Events} of {keys.Length} events");
        Assert.True(shrunk.Events <= 4, $"the shrunk case still has {shrunk.Events} events");
        Assert.Contains("is held there", shrunk.Story);
    }

    /// <summary>
    /// The key of a pause event reads back into the same event, including a later occurrence of the point and a pause by the
    /// other computer, and a key that is not one (no event meanwhile, an unknown event, an unknown or non-computer held event) is not
    /// parsed. (A plain unit test: it runs with the normal suite.)
    /// </summary>
    [Fact]
    public void PauseEvent_KeyRoundTrips()
    {
        var known = EventCatalogue.Build().Concat(SweepEvents.Build()).ToDictionary(e => e.Key);

        foreach (var key in new[]
                 {
                     "Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)",
                     "Pause(Save(bob) at Save.AfterTmpVerify #2; AliceTakesOver)",
                     "Pause(Sleep(alice,10m) at Demote.AfterWorkingCopyHandled; AliceSwitches)",
                     "Pause(Close(bob) at Release.AfterFinalSave; ItRestoresOldFile)",
                     "Pause(Open(bob) at Checkout.AfterLockAcquired; TheLockFileIsDeleted)",
                     "Pause(RestoreBackup(alice) at Restore.AfterTmpVerified; TimePasses)",
                     "Pause(Save(alice) at Save.AfterLocalSnapshot; BobTakesOver)",
                     "Pause(Save(bob) at Save.AfterLocalSnapshot; BobEdits)",
                 })
        {
            var parsed = PauseEvent.TryParse(key, known);
            Assert.NotNull(parsed);
            Assert.Equal(key, parsed!.Key);
        }

        Assert.Null(PauseEvent.TryParse("Save(bob)", known));
        Assert.Null(PauseEvent.TryParse("PowerCut(Save(bob) at Save.AfterRename)", known));
        Assert.Null(PauseEvent.TryParse("Pause(Save(bob) at Save.AfterTmpVerify)", known));                      // no event meanwhile
        Assert.Null(PauseEvent.TryParse("Pause(Save(bob) at Save.AfterTmpVerify; NobodyDoesAnything)", known));  // an unknown event
        Assert.Null(PauseEvent.TryParse("Pause(NoSuchEvent(bob) at Save.AfterTmpVerify; TimePasses)", known));   // an unknown held event
        Assert.Null(PauseEvent.TryParse("Pause(AdvanceTime(4m) at Save.AfterTmpVerify; TimePasses)", known));    // not a computer's event
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Real-time limits: designed waiting is not a violation; a deadlock is a Hang
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// "Bob switches while his own save is held" waits, by design: the switch's release waits for a save in flight, which is held.
    /// After the grace period the save is let go and both finish. That is NOT a violation: the outcome is Waited, the story says the
    /// switch waited for the save, the save succeeded, Bob ended on the other database, and no invariant fired.
    /// </summary>
    [HarnessFact]
    public async Task Pause_BobSwitchesDuringHisOwnHeldSave_WaitsAsDesigned_AndIsNotAViolation()
    {
        using var world = new World(_output, nameof(Pause_BobSwitchesDuringHisOwnHeldSave_WaitsAsDesigned_AndIsNotAViolation));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        Task<SaveOutcome>? save = null;
        var result = await world.RunWithPauseAsync(bob, "Save.AfterTmpVerify", 1, "Bob presses Save",
            operation: () => save = bob.SaveAsync(),
            eventStory: "Bob switches database", eventIsPossible: () => bob.HasOpenSession,
            pauseEvent: async () => await bob.SwitchToAsync(world.EnsureOtherDatabase()),
            limits: QuickLimits);

        Assert.Equal(PauseOutcome.Waited, result.Outcome);
        Assert.Empty(world.Violations);
        Assert.Equal(SaveOutcome.Success, await save!);
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        Assert.Contains("waited for Bob's operation to finish", world.PrintStory(Array.Empty<string>()));
        world.Finish();
    }

    /// <summary>
    /// A planted deadlock: the event waits for the operation to finish, the operation (after it is released) waits for the event.
    /// Neither can finish. The pause gives a Hang violation within its limits, the stuck work is abandoned (Bob's process is
    /// marked dead), the step ends, and the TEST does not hang: the whole thing takes seconds.
    /// </summary>
    [HarnessFact]
    public async Task Pause_APlantedDeadlock_IsAHang_WithinTheLimit_AndTheTestDoesNotHang()
    {
        using var world = new World(_output, nameof(Pause_APlantedDeadlock_IsAHang_WithinTheLimit_AndTheTestDoesNotHang));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        var operationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventFinished     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();

        var result = await world.RunWithPauseAsync(bob, "Save.AfterTmpVerify", 1, "Bob presses Save",
            operation: async () => { await bob.SaveAsync(); await eventFinished.Task; operationFinished.SetResult(); },
            eventStory: "somebody waits for Bob's save", eventIsPossible: () => true,
            pauseEvent: async () => { await operationFinished.Task; eventFinished.SetResult(); },
            eventComputers: null, limits: QuickLimits);
        watch.Stop();

        Assert.Equal(PauseOutcome.Hung, result.Outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"the pause took {watch.Elapsed}: it should end within its limits");
        var hang = Assert.Single(world.Violations, v => v.Kind == ViolationKind.Hang);
        Assert.Equal("Hang", hang.Invariant);
        Assert.Equal(Findings.NewFinding, hang.FindingId);          // a hang is never an expected finding: it fails a generated run
        Assert.False(bob.IsRunning, "the stuck computer must be abandoned");
        Assert.Contains("HANG", world.PrintStory(Array.Empty<string>()));
        world.Finish();
    }

    /// <summary>
    /// The other ways a pause can hang: the operation never returns after it is let go (it waits on something that never comes), and
    /// the event itself never finishes. Both are a Hang within the limits, not a hung test run.
    /// </summary>
    [HarnessFact]
    public async Task Pause_AnOperationOrAnEventThatNeverEnds_IsAHang()
    {
        foreach (var operationNeverReturns in new[] { true, false })
        {
            using var world = new World(_output, $"{nameof(Pause_AnOperationOrAnEventThatNeverEnds_IsAHang)} (operation never returns: {operationNeverReturns})");
            world.ExpectFindings(Findings.NewFinding);
            world.CreateDatabase();
            var bob = world.AddComputer("bob");
            await bob.OpenAsync();
            bob.Edit();

            var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var watch = Stopwatch.StartNew();
            var result = await world.RunWithPauseAsync(bob, "Save.AfterTmpVerify", 1, "Bob presses Save",
                operation: async () => { await bob.SaveAsync(); if (operationNeverReturns) await never.Task; },
                eventStory: "something happens", eventIsPossible: () => true,
                pauseEvent: async () => { if (!operationNeverReturns) await never.Task; },
                limits: QuickLimits);

            Assert.Equal(PauseOutcome.Hung, result.Outcome);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"the pause took {watch.Elapsed}");
            Assert.Single(world.Violations, v => v.Kind == ViolationKind.Hang);
            world.Finish();
        }
    }

    /// <summary>The operation never reaches the point and never returns either (it is stuck before the point): a Hang within the reach limit.</summary>
    [HarnessFact]
    public async Task Pause_AnOperationThatNeverReachesThePoint_AndNeverReturns_IsAHang()
    {
        using var world = new World(_output, nameof(Pause_AnOperationThatNeverReachesThePoint_AndNeverReturns_IsAHang));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();

        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await world.RunWithPauseAsync(bob, "Save.AfterTmpVerify", 1, "Bob does something that never ends",
            operation: () => never.Task, eventStory: "something happens", eventIsPossible: () => true,
            pauseEvent: () => Task.CompletedTask, limits: QuickLimits);

        Assert.Equal(PauseOutcome.Hung, result.Outcome);
        Assert.Single(world.Violations, v => v.Kind == ViolationKind.Hang);
        world.Finish();
    }

    /// <summary>An operation that never reaches the point simply completes: no pause happens, no violation, and the event does not run.</summary>
    [HarnessFact]
    public async Task Pause_AtAPointTheOperationNeverReaches_JustLetsTheOperationHappen()
    {
        using var world = new World(_output, nameof(Pause_AtAPointTheOperationNeverReaches_JustLetsTheOperationHappen));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        var eventRan = false;
        var result = await world.RunWithPauseAsync(bob, "Release.AfterFinalSave", 1, "Bob presses Save",
            operation: () => bob.SaveAsync(), eventStory: "something happens", eventIsPossible: () => true,
            pauseEvent: () => { eventRan = true; return Task.CompletedTask; });

        Assert.Equal(PauseOutcome.NotReached, result.Outcome);
        Assert.False(eventRan);
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I4 is attributed to the computer whose flow reached the point
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// While a pause is in progress two actions overlap, so "the computer acting now" can be the wrong one. The event here leaves it
    /// pointing at ALICE (the way an overlapping action of hers would) and puts her lock on the share; when Bob's save goes on and
    /// reaches the rename, the I4 observation must still be about BOB, the computer whose flow reached the point, and say the lock is
    /// not his. (Before the fix the observer asked "who is acting now" and would have looked at Alice.)
    /// </summary>
    [HarnessFact]
    public async Task Pause_TheRenameCheck_IsAttributedToTheComputerWhoseFlowReachedIt()
    {
        using var world = new World(_output, nameof(Pause_TheRenameCheck_IsAttributedToTheComputerWhoseFlowReachedIt));
        world.ExpectFindings("F12");
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");
        await bob.OpenAsync();
        bob.Edit();

        await world.RunWithPauseAsync(bob, "Save.AfterLocalSnapshot", 1, "Bob presses Save",
            operation: () => bob.SaveAsync(), eventStory: "Alice's lock appears, and the world still thinks Alice is acting",
            eventIsPossible: () => true,
            pauseEvent: () =>
            {
                world.PlaceForeignLock("alice", "ALICE-PC");
                world.CurrentActor = alice;          // an overlapping action of Alice's leaves "who is acting now" pointing at her
                return Task.CompletedTask;
            });

        var violation = Assert.Single(world.Violations, v => v.Kind == ViolationKind.WriteWithoutLockOrOnStaleVersion);
        Assert.Equal("bob", violation.Computer);
        Assert.Contains("not to Bob", violation.Reason);
        Assert.Equal("F12", violation.FindingId);
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // F2, F12 and F16 routes
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// F2: Bob edits while his own save is held after its snapshot. The edit misses the snapshot, the save's last step deletes the
    /// marker, and when Bob's computer crashes before another save, his kept working file has no marker: the edit is nowhere. The
    /// loss is found at the crash and classified F2 (not "new"); nothing else is wrong.
    /// </summary>
    [HarnessFact]
    public async Task F2_AnEditDuringTheOwnSave_IsLostAtTheNextCrash_AndClassifiedF2()
    {
        var run = await RunKeysAsync(F2Route);
        _output.WriteLine(run.Story);

        var lost = Assert.Single(run.Violations);
        Assert.Equal("F2", lost.FindingId);
        Assert.Equal("I2", lost.Invariant);
        Assert.Contains("bob-2", lost.Reason);
        Assert.Empty(run.NewViolations);
        Assert.Equal(PauseOutcome.Interleaved, Assert.Single(run.PauseResults).Outcome);
    }


    /// <summary>
    /// The clean-close face of F2 (spec item 15: "edits made during the exit save are deleted"): Bob closes TermPoint and, while his
    /// final save is held at its last step (the marker is already deleted), makes another edit. The edit is not in the save, and
    /// when the close goes on, the lock is released and the working copy that holds the edit is deleted. No crash is needed: the edit
    /// is gone when Bob's TermPoint has closed. Classified F2, not "new".
    /// </summary>
    [HarnessFact]
    public async Task F2_AnEditDuringTheExitSave_IsDeletedWithTheWorkingCopy_AndClassifiedF2()
    {
        var run = await RunKeysAsync("Open(bob)", "Pause(Close(bob) at Save.AfterMarkerDelete; BobEdits)");
        _output.WriteLine(run.Story);

        var lost = Assert.Single(run.Violations);
        Assert.Equal("F2", lost.FindingId);
        Assert.Equal("I2", lost.Invariant);
        Assert.Empty(run.NewViolations);
    }

    /// <summary>
    /// The same loss when the edit lands after the final save has returned but before the close cleans up (held at
    /// <c>Release.AfterFinalSave</c>). The edit has to wait for the close's lock release, so this pause is "waited"; the loss is F2 all the same.
    /// </summary>
    [HarnessFact]
    public async Task F2_AnEditBetweenTheExitSaveAndTheCleanup_IsDeleted_AndClassifiedF2()
    {
        var run = await RunKeysAsync("Open(bob)", "Pause(Close(bob) at Release.AfterFinalSave; BobEdits)");
        _output.WriteLine(run.Story);

        Assert.Equal("F2", Assert.Single(run.Violations).FindingId);
        Assert.Empty(run.NewViolations);
    }
    /// <summary>
    /// The same edit made BEFORE the save's snapshot (held at <c>Save.AfterSourceCheck</c>) is in the snapshot, so it is saved: no
    /// loss, no violation. Pins that the F2 rule is about the window after the snapshot, not about any edit during a save.
    /// </summary>
    [HarnessFact]
    public async Task F2_AnEditBeforeTheSnapshot_IsInTheSave_AndLosesNothing()
    {
        var run = await RunKeysAsync("Open(bob)", "Edit(bob)", "Pause(Save(bob) at Save.AfterSourceCheck; BobEdits)", "Crash(bob)", "Open(bob)");
        _output.WriteLine(run.Story);

        Assert.Empty(run.Violations);
        Assert.Equal(PauseOutcome.Interleaved, Assert.Single(run.PauseResults).Outcome);
    }

    /// <summary>
    /// F12: Alice takes over and saves while Bob's save is held before it has copied to D.tmp. Bob's save goes on, finds nothing
    /// to stop it, and his rename lands over Alice's save. Every violation the route causes is F12: I4 (the lock was Alice's and
    /// the database was not the version Bob saw, at the rename) and I1 (Alice's committed edit left the shared database, in the
    /// same step). Nothing is "new".
    /// </summary>
    [HarnessFact]
    public async Task F12_ASaveWhoseRenameLandsOverAnotherComputersSave_IsClassifiedF12_ForBothI4AndI1()
    {
        var run = await RunKeysAsync(F12Route);
        _output.WriteLine(run.Story);

        Assert.Contains(run.Violations, v => v.Invariant == "I4" && v.FindingId == "F12");
        Assert.Contains(run.Violations, v => v.Invariant == "I1" && v.FindingId == "F12");
        Assert.Empty(run.NewViolations);
        Assert.All(run.Violations, v => Assert.Equal("F12", v.FindingId));
    }

    /// <summary>
    /// When Alice takes over and saves while Bob's save is held AFTER his D.tmp is verified, her save uses the same D.tmp (the
    /// name is shared) and moves it onto D, so Bob's rename finds no D.tmp and fails: nothing is overwritten, his save reports a copy
    /// error and his edit stays kept. I4 still sees that the lock was Alice's when the rename was about to happen (F12), but no
    /// committed edit is lost. Pins the difference between the two F12 routes.
    /// </summary>
    [HarnessFact]
    public async Task F12_WhenTheOtherSaveTakesTheSharedTempFile_TheRenameFails_AndNothingIsLost()
    {
        var run = await RunKeysAsync("Open(bob)", "Edit(bob)", "Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)", "Crash(bob)", "Open(bob)");
        _output.WriteLine(run.Story);

        Assert.Contains(run.Violations, v => v.Invariant == "I4" && v.FindingId == "F12");
        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I1");
        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I2");
        Assert.Empty(run.NewViolations);
        Assert.Contains("Bob saved the database — CopyError", run.Story);
    }

    /// <summary>
    /// F16 through invariant I6: Bob's computer wakes to find his lock gone, and the demotion that starts is held in the middle while
    /// Bob switches database. When it goes on it copies the NEW database and leaves a session that holds the new lock read-only.
    /// Both clauses fire and are F16: (b) the demotion started on dept.db and finds the session on other.db, and (a) the read-only
    /// session on other.db holds other.db's lock.
    /// </summary>
    [HarnessFact]
    public async Task F16_ADemotionHeldWhileTheUserSwitches_FiresBothClausesOfI6()
    {
        var run = await RunKeysAsync(F16Route);
        _output.WriteLine(run.Story);

        var i6 = run.Violations.Where(v => v.Invariant == "I6").ToList();
        Assert.Equal(2, i6.Count);
        Assert.All(i6, v => Assert.Equal("F16", v.FindingId));
        Assert.Contains(i6, v => v.Reason.Contains("started on dept.db") && v.Reason.Contains("other.db"));
        Assert.Contains(i6, v => v.Reason.Contains("read-only") && v.Reason.Contains("holds a lock it cannot use"));
        Assert.Empty(run.NewViolations);
    }


    /// <summary>
    /// The pause entries of <see cref="KnownSequences"/> (F2, F12, F16) are already as short as they can be: each shows its finding,
    /// has no breach nobody explains, and the shrinker cannot take an event out of it. (A sequence that could be shortened is a
    /// sequence that says more than it needs to.)
    /// </summary>
    [HarnessFact]
    public async Task KnownSequences_OfThePauseFindings_AreAlreadyShrunk()
    {
        foreach (var finding in new[] { "F2", "F12", "F16" })
        {
            var sequence = KnownSequences.All.Single(s => s.Finding == finding);
            var options  = new RunOptions { Title = sequence.Name, ExtraEvents = EventCatalogue.ExtrasFor(sequence.Events) };
            var run      = await RunExecutor.ExecuteAsync(new ReplaySource(sequence.ToRecord()), options);

            var shown = run.Violations.FirstOrDefault(v => v.FindingId == finding);
            Assert.NotNull(shown);
            Assert.Empty(run.NewViolations);

            var shrunk = await Shrinker.ShrinkAsync(run.Record.Take(shown!.EventIndex), shown.Signature, options);
            _output.WriteLine($"{finding}: {sequence.Events.Count} events listed, {shrunk.Events} after shrinking.\n{shrunk.Story}");
            Assert.True(shrunk.Reproduced);
            Assert.True(shrunk.Events >= sequence.Events.Count,
                $"{finding}'s known sequence can be shortened to {shrunk.Events} events: {string.Join(", ", shrunk.Record.Steps.Select(s => s.EventKey))}");
        }
    }
    /// <summary>
    /// I6 does NOT fire on an ordinary demotion that nobody interrupts: Bob's computer wakes to find his lock gone, the demotion runs
    /// to the end on dept.db, and Bob is a read-only reader that holds no lock.
    /// </summary>
    [HarnessFact]
    public async Task I6_AnOrdinaryDemotion_IsNotAViolation()
    {
        var run = await RunKeysAsync("Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)", "Sleep(bob,10m)");
        _output.WriteLine(run.Story);

        Assert.DoesNotContain(run.Violations, v => v.Invariant == "I6");
        Assert.Empty(run.NewViolations);
    }

    /// <summary>
    /// I6 clause (a) on a planted state: Bob's session is made read-only (D'' is set up, as a demotion does) while his lock service
    /// still says he is the writer and the lock file still names his session. I6 reports that he holds a lock he cannot use, and it
    /// is classified F16.
    /// </summary>
    [HarnessFact]
    public async Task I6_AReadOnlySessionThatStillHoldsItsLock_IsCaught()
    {
        using var world = new World(_output, nameof(I6_AReadOnlySessionThatStillHoldsItsLock_IsCaught));
        world.ExpectFindings("F16");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        Assert.True(bob.Locks.IsWriter);

        await world.DoAsync(bob, "PLANTED: Bob's checkout is made read-only, but his lock is not given back",
            async () => { Assert.NotNull(await bob.Checkout.SetupReadOnlySnapshotAsync()); });

        var violation = Assert.Single(world.Violations, v => v.Invariant == "I6");
        Assert.Equal(ViolationKind.ReadOnlySessionHoldsLock, violation.Kind);
        Assert.Equal("F16", violation.FindingId);
        Assert.Contains("lock service says it is the writer", violation.Reason);
        Assert.Contains("the lock file of dept.db names its session", violation.Reason);
        world.Finish();
    }

    /// <summary>
    /// I6 clause (b) on a planted state: a demotion starts on dept.db and, at its next point, the session has been moved to another
    /// database. The clause fires although the session is not (yet) read-only to the checkout service, so it is independent of clause (a).
    /// </summary>
    [HarnessFact]
    public async Task I6_ADemotionWhoseSessionMovesToAnotherDatabase_IsCaught()
    {
        using var world = new World(_output, nameof(I6_ADemotionWhoseSessionMovesToAnotherDatabase_IsCaught));
        world.ExpectFindings("F16");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();
        world.AdvanceTime(TimeSpan.FromMinutes(4));
        world.PlaceForeignLock("alice", "ALICE-PC");   // Bob's next lock check finds his lock gone

        // At the demotion's second point the session is switched (planted) to the other database.
        world.AtCrashPoint("Demote.AfterConnectionClosed", () => PlantSourcePath(bob.Checkout, world.EnsureOtherDatabase()));
        await bob.SaveAsync();                          // the lock check fails, the demotion runs inline

        var violation = Assert.Single(world.Violations, v => v.Kind == ViolationKind.DemotionOnAnotherDatabase);
        Assert.Equal("I6", violation.Invariant);
        Assert.Equal("F16", violation.FindingId);
        Assert.Equal("bob", violation.Computer);
        Assert.Contains("started on dept.db", violation.Reason);
        world.Finish();
    }

    /// <summary>Changes a checkout's source path behind its back (the setter is private), to plant "the session moved to another database".</summary>
    /// <param name="checkout">The checkout service.</param>
    /// <param name="path">The new source path.</param>
    private static void PlantSourcePath(CheckoutService checkout, string path)
    {
        var field = typeof(CheckoutService).GetField("<SourcePath>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CheckoutService.SourcePath has no backing field to plant.");
        field.SetValue(checkout, path);
    }
}
