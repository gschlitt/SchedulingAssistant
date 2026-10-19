using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Five hand-written walks of the multi-computer harness (spec item 9.3, step 9.3a): the rows of the hand smoke test
/// (spec item 9.1d), automated. Each walk plays Bob and Alice (and sometimes a colleague) through a story in a
/// <see cref="World"/>, using the real <see cref="SessionLifecycle"/>, <see cref="CheckoutService"/> and
/// <see cref="WriteLockService"/> on real files in a temp folder, and asserts concrete outcomes: which traceable edits
/// ("bob-1") are in the shared database, in a computer's kept working file or in an exported copy; which dialogs were
/// shown, by exact title; and which files are left.
///
/// <para>The walks pin what TermPoint does TODAY, including behaviour the data-persistence review calls wrong (for
/// example the "Can't Restore" detour after a takeover). They run only when the environment variable
/// <c>TERMPOINT_HARNESS=1</c> is set (<see cref="HarnessFactAttribute"/>), so the normal suite is unchanged.</para>
///
/// <para>A crash in a walk is "the process was killed between actions" (<see cref="World.CrashComputer"/>);
/// crashes in the middle of an operation come with step 9.3c.</para>
/// </summary>
public sealed class HarnessWalkTests
{
    /// <summary>The test output, where each walk's story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public HarnessWalkTests(ITestOutputHelper output) => _output = output;
    /// <summary>The title of the stale-lock question.</summary>
    private const string StaleLockTitle = "Database Lock Is Inactive";

    /// <summary>The title of the question asked when a previous session left unsaved changes.</summary>
    private const string RestoreTitle = "Restore Unsaved Changes?";

    /// <summary>The title of the question asked when the kept changes cannot be restored.</summary>
    private const string CantRestoreTitle = "Can't Restore Unsaved Changes";

    /// <summary>The title of the save-file picker for a recovered copy.</summary>
    private const string RecoveredCopyPickerTitle = "Save Recovered Copy";

    /// <summary>Lists what a computer's prompts were asked, as "Kind:Title", in order.</summary>
    /// <param name="computer">The computer.</param>
    private static string[] AskedBy(Computer computer)
        => computer.Prompts.Calls.Select(c => $"{c.Kind}:{c.Title}").ToArray();

    /// <summary>A path for an exported copy, in a folder that is not on the share.</summary>
    /// <param name="world">The world.</param>
    /// <param name="fileName">The file name.</param>
    private static string ExportPath(World world, string fileName)
    {
        var dir = Path.Combine(world.RootDir, "exports");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    /// <summary>The lock file beside the shared database.</summary>
    /// <param name="world">The world.</param>
    private static string LockFile(World world) => Path.ChangeExtension(world.DatabasePath, ".lock");

    // ═════════════════════════════════════════════════════════════════════════
    // Walk 1: the ordinary day
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob opens, edits, saves and closes; Alice then opens and sees Bob's edit. Nothing is asked of either of them,
    /// the lock is free after each close, and Bob leaves no working files behind.
    /// </summary>
    [HarnessFact]
    public async Task Walk1_BobSavesAndCloses_AliceSeesHisEdit()
    {
        using var world = new World(_output, "Walk1_BobSavesAndCloses_AliceSeesHisEdit");
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");

        await bob.OpenAsync();
        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);
        Assert.True(File.Exists(LockFile(world)), "Bob holds the lock while he works");

        var edit = bob.Edit();
        Assert.Equal("bob-1", edit);
        Assert.True(bob.HasDirtyMarker, "an edit raises the unsaved-changes marker");
        Assert.Empty(world.EditsInDatabase());                       // not saved yet
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());     // only in his working copy

        Assert.Equal(SaveOutcome.Success, await bob.SaveAsync());
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        Assert.False(bob.HasDirtyMarker, "after the save the changes are in D, so the marker is gone");

        await bob.CloseAsync();
        Assert.False(File.Exists(LockFile(world)), "closing gives the lock back");
        Assert.False(bob.HasWorkingFile);
        Assert.False(bob.HasDirtyMarker);

        await alice.OpenAsync();
        Assert.Equal(CheckoutMode.WriteAccess, alice.Checkout.Mode);
        Assert.Equal(new[] { "bob-1" }, alice.WorkingFileEdits());   // Alice's fresh copy of D holds Bob's edit
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());

        // Switching to the same database (what "Take over as writer" does) ends one session and starts another.
        Assert.True(await alice.SwitchToAsync(world.DatabasePath));
        Assert.Equal(CheckoutMode.WriteAccess, alice.Checkout.Mode);
        Assert.Equal(new[] { "bob-1" }, alice.WorkingFileEdits());

        await alice.CloseAsync();
        Assert.Empty(AskedBy(bob));
        Assert.Empty(AskedBy(alice));
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Walk 2: crash, restart, restore
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob edits, then his TermPoint is killed. Bob starts it again on the same computer: his dead process is
    /// recognised, so the lock is reclaimed at once; he is offered his unsaved changes, chooses Restore, and — because
    /// nobody else has touched D — the open saves them: bob-1 reaches D.
    /// </summary>
    [HarnessFact]
    public async Task Walk2_BobCrashes_RestartsAndRestores_TheOpenSavesHisWork()
    {
        using var world = new World(_output, "Walk2_BobCrashes_RestartsAndRestores_TheOpenSavesHisWork");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        Assert.True(bob.HasDirtyMarker);
        Assert.Empty(world.EditsInDatabase());

        world.CrashComputer(bob);

        // What a killed process leaves behind: his lock, his working copy with its edit, and its marker.
        Assert.True(File.Exists(LockFile(world)));
        Assert.True(bob.HasWorkingFile);
        Assert.True(bob.HasDirtyMarker);
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());
        Assert.Empty(world.EditsInDatabase());

        bob.Relaunch();
        bob.Prompts.ExpectChoice(
            RestoreTitle, true,
            "Unsaved changes from a previous session with this database were kept.\n\nWould you like to restore them?");
        await bob.OpenAsync();

        Assert.Equal(new[] { $"Choice:{RestoreTitle}" }, AskedBy(bob));
        Assert.Equal("Restore|Discard", bob.Prompts.Calls.Single().Buttons);
        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);
        Assert.True(bob.Checkout.ReclaimedDeadSession, "same computer, dead process: the lock is reclaimed without a wait");
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());     // the open saved the restored work
        Assert.False(bob.HasDirtyMarker);

        await bob.CloseAsync();
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Walk 3: crash, a colleague takes over, the crashed user comes back
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob edits and his TermPoint is killed; his lock stays and its heartbeat freezes. Five minutes pass. Alice opens:
    /// "Database Lock Is Inactive", she takes over (the confirmation wait runs on the fake clock) and gets write
    /// access; she edits, saves and closes. Bob starts TermPoint again: he is offered his unsaved changes, chooses
    /// Restore, is told they cannot be restored because the database changed, and saves a copy. bob-1 is in the
    /// exported file only, alice-1 is in D, and Bob's kept files are gone.
    ///
    /// <para><b>Adapted from the brief:</b> Alice CLOSES before Bob restarts. While she still holds the lock, Bob's
    /// restore is refused earlier (the lock comes before the conflict check) and he is shown the read-only "Unsaved
    /// Changes Kept" message instead of "Can't Restore".</para>
    /// </summary>
    [HarnessFact]
    public async Task Walk3_BobCrashes_AliceTakesOver_BobComesBack_CantRestore_SavesACopy()
    {
        using var world = new World(_output, "Walk3_BobCrashes_AliceTakesOver_BobComesBack_CantRestore_SavesACopy");
        world.ExpectFindings("F7");   // "Restore?" is asked, then "Can't Restore" (the review's F7)
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");

        await bob.OpenAsync();
        bob.Edit();
        world.CrashComputer(bob);
        Assert.True(File.Exists(LockFile(world)), "the killed process left its lock behind");

        world.AdvanceTime(TimeSpan.FromMinutes(5));

        // Alice: the lock is stale -> she is asked, takes over, and the confirmation wait runs on the fake clock.
        alice.Prompts
            .ExpectChoice(StaleLockTitle, true)
            .ExpectStaleLockWait(true, before: () =>
                world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.StaleConfirmationSeconds + 1)));
        await alice.OpenAsync();

        Assert.Equal(new[] { $"Choice:{StaleLockTitle}", "StaleLockWait:" }, AskedBy(alice));
        Assert.Equal("Take over|Open read-only", alice.Prompts.Calls[0].Buttons);
        Assert.Contains("bob on BOB-PC", alice.Prompts.Calls[0].Text);
        Assert.Equal(CheckoutMode.WriteAccess, alice.Checkout.Mode);
        Assert.Empty(world.EditsInDatabase());                       // Bob's work never reached D

        alice.Edit();
        await alice.AutoSaveTickAsync();                             // Alice's autosave timer fires
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());
        await alice.CloseAsync();
        Assert.False(File.Exists(LockFile(world)));

        // Bob comes back: his kept work cannot be restored, because D changed since he last saw it.
        var export = ExportPath(world, "bob-recovered.db");
        bob.Relaunch();
        Assert.True(bob.HasDirtyMarker, "Bob's kept changes are still on his disk");
        bob.Prompts
            .ExpectChoice(RestoreTitle, true)
            .ExpectChoice(CantRestoreTitle, true)
            .ExpectPick(RecoveredCopyPickerTitle, export);
        await bob.OpenAsync();

        Assert.Equal(
            new[] { $"Choice:{RestoreTitle}", $"Choice:{CantRestoreTitle}", $"PickSaveFile:{RecoveredCopyPickerTitle}" },
            AskedBy(bob));
        Assert.Equal("Save a Copy…|Discard", bob.Prompts.Calls[1].Buttons);
        Assert.Equal(new[] { "bob-1" }, world.EditsInFile(export));                // his work is in the exported copy only
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());                // D is Alice's
        Assert.False(bob.HasDirtyMarker, "his kept files were deleted once the copy landed");
        Assert.Equal(new[] { "alice-1" }, bob.WorkingFileEdits());                 // he now works on a fresh copy of D
        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);

        await bob.CloseAsync();
        Assert.False(bob.HasWorkingFile);
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Walk 4: Bob's network drops, Alice takes over, Bob's network returns
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob opens and edits; his connection to the share drops. His heartbeat is ticked once while the network is down
    /// and fails (below the failure threshold, so nothing escalates); five more minutes pass with no heartbeat. Alice opens, finds the lock stale and takes over; she
    /// edits and saves. Bob's network comes back and his next save finds Alice's lock: Bob is demoted in place — the
    /// banner names Alice, his working copy and marker are kept. Bob closes; Alice closes; Bob opens again and is
    /// offered his kept changes. Pinned as today: "Restore?" then "Can't Restore" (D changed), and he saves a copy.
    /// </summary>
    [HarnessFact]
    public async Task Walk4_BobsNetworkDrops_AliceTakesOver_BobIsDemoted_ThenOffersHisKeptChanges()
    {
        using var world = new World(_output, "Walk4_BobsNetworkDrops_AliceTakesOver_BobIsDemoted_ThenOffersHisKeptChanges");
        world.ExpectFindings("F6", "F7");   // the lock-lost banner promises a restore that then cannot be done (F6); "Restore?" then "Can't Restore" (F7)
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");

        await bob.OpenAsync();
        bob.Edit();

        // A heartbeat on a healthy network renews the lock file (control) ...
        world.AdvanceTime(TimeSpan.FromMinutes(1));
        var lockAtOpen = File.ReadAllText(LockFile(world));
        await bob.HeartbeatTickAsync();
        var lockAfterHeartbeat = File.ReadAllText(LockFile(world));
        Assert.NotEqual(lockAtOpen, lockAfterHeartbeat);

        // ... but once the network is down the same tick fails (one failure, below the escalation threshold, so
        // nothing else happens) and the heartbeat stands still while four more minutes pass.
        world.NetworkDown(bob);
        world.AdvanceTime(TimeSpan.FromMinutes(1));
        await bob.HeartbeatTickAsync();
        Assert.Equal(lockAfterHeartbeat, File.ReadAllText(LockFile(world)));
        world.AdvanceTime(TimeSpan.FromMinutes(4));

        alice.Prompts
            .ExpectChoice(StaleLockTitle, true)
            .ExpectStaleLockWait(true, before: () =>
                world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.StaleConfirmationSeconds + 1)));
        await alice.OpenAsync();
        Assert.Equal(new[] { $"Choice:{StaleLockTitle}", "StaleLockWait:" }, AskedBy(alice));
        Assert.Equal(CheckoutMode.WriteAccess, alice.Checkout.Mode);

        alice.Edit();
        Assert.Equal(SaveOutcome.Success, await alice.SaveAsync());
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());

        // Bob's network is back; his save finds the lock is Alice's.
        world.NetworkUp(bob);
        Assert.Equal(SaveOutcome.LockLost, await bob.SaveAsync());

        var banner = Assert.Single(bob.Host.LockLossSurface.SaveErrors);
        Assert.Equal(
            "Write access was taken over by alice on ALICE-PC. " +
            "Your unsaved changes have been kept — TermPoint will offer to restore " +
            "them the next time you open this database with write access. " +
            "You can regain write access by exiting and restarting TermPoint.",
            banner);
        Assert.Equal(CheckoutMode.ReadOnly, bob.Checkout.Mode);
        Assert.True(bob.HasWorkingFile, "D' is kept");
        Assert.True(bob.HasDirtyMarker, "and so is its marker");
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());   // Bob's save wrote nothing
        Assert.Equal("alice", System.Text.Json.JsonSerializer
            .Deserialize<TermPoint.Models.LockFileData>(File.ReadAllText(LockFile(world)))!.Username);

        await bob.CloseAsync();
        Assert.True(bob.HasWorkingFile, "closing a demoted session keeps the only copy of his work");
        Assert.True(bob.HasDirtyMarker);
        await alice.CloseAsync();
        Assert.False(File.Exists(LockFile(world)));

        // Later, Bob opens again.
        var export = ExportPath(world, "bob-after-demotion.db");
        bob.Relaunch();
        bob.Prompts
            .ExpectChoice(RestoreTitle, true)
            .ExpectChoice(CantRestoreTitle, true)
            .ExpectPick(RecoveredCopyPickerTitle, export);
        await bob.OpenAsync();

        Assert.Equal(
            new[] { $"Choice:{RestoreTitle}", $"Choice:{CantRestoreTitle}", $"PickSaveFile:{RecoveredCopyPickerTitle}" },
            AskedBy(bob));
        Assert.Equal(new[] { "bob-1" }, world.EditsInFile(export));
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());
        Assert.False(bob.HasDirtyMarker);
        Assert.Equal(new[] { "alice-1" }, bob.WorkingFileEdits());

        await bob.CloseAsync();
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Walk 5: a colleague changes the database under Bob (the save conflict)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob has unsaved edits when a colleague changes D. Bob's save is refused (the conflict banner appears). He chooses
    /// Save a Copy…: "Your Version Was Saved", his session reopens on the colleague's version, and bob-1 exists only in
    /// the exported file.
    /// </summary>
    [HarnessFact]
    public async Task Walk5_ColleagueChangesTheDatabase_BobSavesACopy_AndReopensOnTheirVersion()
    {
        using var world = new World(_output, "Walk5_ColleagueChangesTheDatabase_BobSavesACopy_AndReopensOnTheirVersion");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.ColleagueWritesDatabase("colleague-1");
        Assert.False(bob.ConflictBannerShown);

        Assert.Equal(SaveOutcome.SourceModified, await bob.SaveAsync());
        Assert.True(bob.ConflictBannerShown);
        Assert.True(bob.Checkout.HasSaveConflict);
        Assert.Equal(new[] { "colleague-1" }, world.EditsInDatabase());   // Bob's save wrote nothing

        var export = ExportPath(world, "bobs-version.db");
        bob.Prompts
            .ExpectPick(RecoveredCopyPickerTitle, export)
            .ExpectWait(SaveConflictPrompt.SavingTitle, SaveConflictPrompt.SavingMessage)
            .ExpectMessage(SaveConflictPrompt.SavedTitle, SaveConflictPrompt.SavedMessage("bobs-version.db"));
        await bob.ResolveConflictAsync(saveCopy: true);

        Assert.Equal(
            new[]
            {
                $"PickSaveFile:{RecoveredCopyPickerTitle}",
                $"Wait:{SaveConflictPrompt.SavingTitle}",
                $"Message:{SaveConflictPrompt.SavedTitle}"
            },
            AskedBy(bob));
        Assert.Equal("Your Version Was Saved", SaveConflictPrompt.SavedTitle);
        Assert.Equal(new[] { "bob-1" }, world.EditsInFile(export));              // his version, as a separate file
        Assert.Equal(new[] { "colleague-1" }, world.EditsInDatabase());          // D is the colleague's, untouched
        Assert.Equal(new[] { "colleague-1" }, bob.WorkingFileEdits());           // reopened on the colleague's version
        Assert.False(bob.HasDirtyMarker, "his kept changes were deleted once the copy landed");
        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);
        Assert.False(bob.Checkout.HasSaveConflict);
        Assert.False(bob.ConflictBannerShown);

        await bob.CloseAsync();
        Assert.Equal(new[] { "colleague-1" }, world.EditsInDatabase());
        Assert.False(File.Exists(LockFile(world)));
        world.AssertAllScriptsConsumed();
        world.Finish();
    }
}
