using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for "the other computer takes over, then closes, while the writer's computer is asleep" (found by hand in the
/// 1.2.4 release field test M5 (4), 2026-11-12).
///
/// <para><b>The story.</b> Bob is the writer and has an unsaved edit; his computer sleeps. Alice opens the database, takes the
/// stale lock over, perhaps edits and saves, then closes TermPoint, which deletes the lock file. When Bob's computer wakes there is
/// no lock file at all. TermPoint cannot tell that from antivirus or cloud-sync software deleting it, so it puts the lock back and
/// carries on as writer (<c>CheckoutService.TryRecoverRemovedLockAsync</c>). That is safe ONLY because every save then checks that
/// the shared database is exactly as Bob last saw it (rule 2): if Alice saved, Bob's save must be refused (SourceModified) and her
/// edit must survive; if she changed nothing, Bob's save may go through.</para>
///
/// <para><b>Why these are named tests.</b> Before 2026-11-12 the harness could not produce this route on purpose: its
/// <see cref="SleepEvent"/> sleeps and wakes in one step, and the pause action "Alice takes over" (<see cref="TakesOverAction"/>)
/// never closes Alice's TermPoint afterwards. A route the search cannot reach must be written down, so these replay it every
/// harness run. Bob's computer being frozen is <c>AdvanceTime(4m)</c> (no timer of his fires); his wake is either his next save or
/// a wake check (<c>Sleep(bob,4m)</c>, whose wake check runs after the clock jump).</para>
///
/// <para>They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).</para>
///
/// <para><b>No message when the lock is put back (1.2.4, 2026-11-12).</b> The field test showed Bob "Another program removed the
/// file … (antivirus or cloud-sync software can do this). TermPoint has put it back…", which named a wrong cause and gave him nothing
/// to do. The notice was dropped; every route here asserts that it never appears. When Alice saved, Bob's one message is the save's
/// conflict.</para>
/// </summary>
public sealed class TakeoverThenReleaseProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public TakeoverThenReleaseProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>Bob opens, edits, and his computer freezes for four minutes; Alice takes the stale lock over.</summary>
    private static readonly string[] BobFrozenAliceTakesOver =
    {
        "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)"
    };

    /// <summary>
    /// Replays <see cref="BobFrozenAliceTakesOver"/> followed by <paramref name="rest"/> (every dialog gets its simplest answer)
    /// and writes the story to the test output.
    /// </summary>
    /// <param name="rest">The event keys after Alice's takeover, in order.</param>
    /// <returns>The run's result.</returns>
    private async Task<RunResult> RunAsync(params string[] rest)
    {
        var keys   = BobFrozenAliceTakesOver.Concat(rest).ToArray();
        var record = new RunRecord();
        foreach (var key in keys) record.Steps.Add(new RecordedStep { EventKey = key });
        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record),
            new RunOptions { Title = "a takeover-then-release proof", ExtraEvents = EventCatalogue.ExtrasFor(keys) });
        _output.WriteLine(run.Story);
        return run;
    }

    /// <summary>The route really happened: Alice took the lock over and her close released it.</summary>
    /// <param name="run">The run.</param>
    private static void AssertAliceTookOverAndReleased(RunResult run)
    {
        Assert.DoesNotContain("Alice starts TermPoint and opens the database — skipped", run.Story);
        Assert.DoesNotContain("Alice closes TermPoint — skipped", run.Story);
    }

    /// <summary>
    /// No data rule is broken: nothing committed disappears (I1), nothing applied is lost (I2), never two writers. And the lock being
    /// put back shows Bob no message (the old "Another program removed the file…" notice).
    /// </summary>
    /// <param name="run">The run.</param>
    private static void AssertNothingLost(RunResult run)
    {
        Assert.DoesNotContain(run.Violations, v => v.Invariant is "I1" or "I2" or "I4" or "I9");
        Assert.Empty(run.NewViolations);
        Assert.DoesNotContain("Another program removed the file", run.Story);
        Assert.DoesNotContain("has put it back", run.Story);
    }

    /// <summary>
    /// Alice takes over, changes nothing and closes; Bob saves. The lock file is gone, so Bob's save puts it back, finds the shared
    /// database exactly as he last saw it, and goes through: his edit is in the shared database and nothing is lost.
    /// </summary>
    [HarnessFact]
    public async Task AliceTakesOverAndClosesWithoutEditing_BobsSave_PutsTheLockBack_AndLands()
    {
        var run = await RunAsync("Close(alice)", "Save(bob)");

        AssertAliceTookOverAndReleased(run);
        AssertNothingLost(run);
        Assert.Contains("Bob saved the database — Success", run.Story);
        Assert.Matches("bob-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// THE DANGEROUS CASE. Alice takes over, edits, saves and closes; Bob saves. The lock file is gone, so Bob's save may put it back,
    /// but the shared database changed since Bob last saw it, so his save must be REFUSED (SourceModified): Alice's committed edit stays
    /// in the shared database (I1) and Bob's edit is kept, not lost (I2). A save that went through here would overwrite Alice's work.
    /// </summary>
    [HarnessFact]
    public async Task AliceTakesOverSavesAndCloses_BobsSave_IsRefused_AndAlicesEditSurvives()
    {
        var run = await RunAsync("Edit(alice)", "Save(alice)", "Close(alice)", "Save(bob)");

        AssertAliceTookOverAndReleased(run);
        AssertNothingLost(run);
        Assert.Contains("Alice saved the database — Success", run.Story);
        Assert.Contains("Bob saved the database — SourceModified", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
        Assert.DoesNotMatch("bob-1: .*the shared database", run.Story);
    }

    /// <summary>The dangerous case with Bob's autosave, not his Save button, as the first save after the wake: refused the same way.</summary>
    [HarnessFact]
    public async Task AliceTakesOverSavesAndCloses_BobsAutosave_IsRefused_AndAlicesEditSurvives()
    {
        var run = await RunAsync("Edit(alice)", "Save(alice)", "Close(alice)", "AutosaveTick(bob)");

        AssertAliceTookOverAndReleased(run);
        AssertNothingLost(run);
        Assert.Contains("Alice saved the database — Success", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
        Assert.DoesNotMatch("bob-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// Alice takes over, changes nothing and closes; Bob's computer then wakes (the wake check finds no lock file and puts it back)
    /// and Bob saves: as on the field test, the save goes through and nothing is lost.
    /// </summary>
    [HarnessFact]
    public async Task AliceTakesOverAndClosesWithoutEditing_BobWakes_ThenSaves_AndLands()
    {
        var run = await RunAsync("Close(alice)", "Sleep(bob,4m)", "Save(bob)");

        AssertAliceTookOverAndReleased(run);
        AssertNothingLost(run);
        Assert.Contains("Bob saved the database — Success", run.Story);
        Assert.Matches("bob-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// The classic takeover, Alice STILL OPEN: Alice takes over, edits and saves, and is still editing when Bob presses Save. The lock
    /// file names Alice, so it is never put back: Bob's save is refused (LockLost), Bob is made read-only with his edit kept, and Alice's
    /// edit stays in the shared database.
    /// </summary>
    [HarnessFact]
    public async Task AliceTakesOverAndSaves_StillOpen_BobsSave_IsRefused_AndAlicesEditSurvives()
    {
        var run = await RunAsync("Edit(alice)", "Save(alice)", "Save(bob)");

        Assert.DoesNotContain("Alice starts TermPoint and opens the database — skipped", run.Story);
        AssertNothingLost(run);
        Assert.Contains("Alice saved the database — Success", run.Story);
        Assert.Contains("Bob saved the database — LockLost", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
        Assert.DoesNotMatch("bob-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// The classic takeover through the wake check, Alice STILL OPEN: Alice takes over, edits and saves; Bob's computer wakes. The wake
    /// check finds Alice's lock, so Bob is made read-only (the "taken over" banner) before he can save; a Save after that writes nothing.
    /// Alice's edit stays in the shared database and Bob's is kept.
    /// </summary>
    [HarnessFact]
    public async Task AliceTakesOverAndSaves_StillOpen_BobWakes_IsMadeReadOnly_AndCannotSaveOverAlice()
    {
        var run = await RunAsync("Edit(alice)", "Save(alice)", "Sleep(bob,4m)", "Save(bob)");

        Assert.DoesNotContain("Alice starts TermPoint and opens the database — skipped", run.Story);
        AssertNothingLost(run);
        Assert.Contains("Alice saved the database — Success", run.Story);
        Assert.Contains("has taken over editing this database", run.Story);   // Bob's lock-lost banner names Alice
        Assert.DoesNotContain("Bob saved the database — Success", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
        Assert.DoesNotMatch("bob-1: .*the shared database", run.Story);
    }

    /// <summary>
    /// The dangerous case through the wake check: Alice takes over, saves and closes; Bob's computer wakes (the wake check puts the
    /// missing lock file back), then Bob saves. The save must still be refused, because the shared database changed.
    /// </summary>
    [HarnessFact]
    public async Task AliceTakesOverSavesAndCloses_BobWakes_ThenSaves_IsRefused_AndAlicesEditSurvives()
    {
        var run = await RunAsync("Edit(alice)", "Save(alice)", "Close(alice)", "Sleep(bob,4m)", "Save(bob)");

        AssertAliceTookOverAndReleased(run);
        AssertNothingLost(run);
        Assert.Contains("Alice saved the database — Success", run.Story);
        Assert.Contains("Bob saved the database — SourceModified", run.Story);
        Assert.Matches("alice-1: .*the shared database", run.Story);
        Assert.DoesNotMatch("bob-1: .*the shared database", run.Story);
    }
}
