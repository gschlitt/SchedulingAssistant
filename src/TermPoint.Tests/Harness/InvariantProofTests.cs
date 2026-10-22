using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// One proof test per invariant of the multi-computer harness (spec item 9.3, step 9.3b): each makes the thing the
/// invariant exists to catch really happen, and asserts that the checker fires, with the right invariant id and the
/// right finding. A checker that is never seen to fail proves nothing; these prove that it can.
///
/// <para>Each test declares the finding it expects (<see cref="World.ExpectFindings"/>), so the violation is recorded
/// instead of stopping the walk, and asserts the recorded <see cref="Violation"/> rather than failing. The tests run
/// only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).</para>
/// </summary>
public sealed class InvariantProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public InvariantProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>The single violation of the given invariant a world recorded.</summary>
    /// <param name="world">The world.</param>
    /// <param name="invariant">"I1" to "I5".</param>
    private static Violation Only(World world, string invariant)
        => Assert.Single(world.Violations, v => v.Invariant == invariant);

    // ═════════════════════════════════════════════════════════════════════════
    // I1: nothing committed disappears
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob saves bob-1 and closes. An older copy of D (made before the save) is then copied over D behind TermPoint's
    /// back (a raw file copy, NOT <see cref="World.ItRestoresOldFile"/>). At the next step I1 reports that bob-1 has
    /// disappeared from the shared database. Classified "new": no finding explains it.
    /// </summary>
    [HarnessFact]
    public async Task I1_AnOlderFileCopiedOverD_BehindTheirBack_IsCaught()
    {
        using var world = new World(_output, nameof(I1_AnOlderFileCopiedOverD_BehindTheirBack_IsCaught));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var older = world.TakeSnapshot();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        Assert.Equal(SaveOutcome.Success, await bob.SaveAsync());
        await bob.CloseAsync();
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());

        File.WriteAllBytes(world.DatabasePath, older);       // behind TermPoint's back: nothing in the story says so
        world.AdvanceTime(TimeSpan.FromMinutes(1));          // the next funnel step looks

        var violation = Only(world, "I1");
        Assert.Equal(ViolationKind.SharedEditsDisappeared, violation.Kind);
        Assert.Contains("bob-1", violation.Reason);
        Assert.Equal(Findings.NewFinding, violation.FindingId);
        world.Finish();
    }

    /// <summary>
    /// The same replacement done as the explicit world event <see cref="World.ItRestoresOldFile"/> is an allowed reset:
    /// no violation.
    /// </summary>
    [HarnessFact]
    public async Task I1_ItRestoringAnOldFile_IsAnAllowedReset()
    {
        using var world = new World(_output, nameof(I1_ItRestoringAnOldFile_IsAnAllowedReset));
        world.CreateDatabase();
        var older = world.TakeSnapshot();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        await bob.SaveAsync();
        await bob.CloseAsync();

        world.ItRestoresOldFile(older);
        world.AdvanceTime(TimeSpan.FromMinutes(1));

        Assert.Empty(world.Violations);
        world.Finish();
    }

    /// <summary>
    /// A successful restore from a backup also resets the I1 baseline: D loses bob-1 on purpose, and nothing is reported.
    /// (Bob's stale session still holds bob-1 in its live working file, so I2 is satisfied too.)
    /// </summary>
    [HarnessFact]
    public async Task I1_ARestoreFromABackup_IsAnAllowedReset()
    {
        using var world = new World(_output, nameof(I1_ARestoreFromABackup_IsAnAllowedReset));
        world.CreateDatabase();
        var backup = Path.Combine(world.RootDir, "backup.db");
        File.WriteAllBytes(backup, world.TakeSnapshot());
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        Assert.Equal(SaveOutcome.Success, await bob.SaveAsync());
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());

        var result = await bob.RestoreFromBackupAsync(backup);
        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.Empty(world.EditsInDatabase());

        world.AdvanceTime(TimeSpan.FromMinutes(1));
        Assert.Empty(world.Violations);
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I2: nothing applied is lost
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob edits and crashes; his kept working file and its marker are then deleted behind TermPoint's back. bob-1 is
    /// nowhere any more: I2 fires. Classified "new" (nobody answered a dialog).
    /// </summary>
    [HarnessFact]
    public async Task I2_KeptFileDeletedBehindTheirBack_IsCaught()
    {
        using var world = new World(_output, nameof(I2_KeptFileDeletedBehindTheirBack_IsCaught));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.CrashComputer(bob);
        Assert.True(bob.HasWorkingFile && bob.HasDirtyMarker);

        foreach (var file in Directory.GetFiles(bob.WorkDir)) File.Delete(file);   // working file, -wal, -shm, marker
        world.AdvanceTime(TimeSpan.FromMinutes(1));

        var violation = Only(world, "I2");
        Assert.Equal(ViolationKind.EditLost, violation.Kind);
        Assert.Equal("bob-1", violation.Edit);
        Assert.Equal(Findings.NewFinding, violation.FindingId);
        world.Finish();
    }

    /// <summary>
    /// A kept working file whose marker is gone is NOT a safe place (the next start deletes it as "nothing was edited"):
    /// I2 fires although the file still holds bob-1.
    /// </summary>
    [HarnessFact]
    public async Task I2_KeptFileWithoutItsMarker_IsNotASafePlace()
    {
        using var world = new World(_output, nameof(I2_KeptFileWithoutItsMarker_IsNotASafePlace));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.CrashComputer(bob);
        File.Delete(bob.MarkerFile);
        world.AdvanceTime(TimeSpan.FromMinutes(1));

        Assert.True(bob.HasWorkingFile);
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());
        Assert.Equal("bob-1", Only(world, "I2").Edit);
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I3: the database is whole
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob saves and closes; bytes in the middle of D (the first bytes of a table's page) are then overwritten behind
    /// TermPoint's back. D fails <c>PRAGMA integrity_check</c>: I3 fires. Classified "new". (The edit readers cannot
    /// read a damaged D; they say "cannot tell" instead of piling more violations on it.)
    /// </summary>
    [HarnessFact]
    public async Task I3_DamagedBytesInTheMiddleOfD_AreCaught()
    {
        using var world = new World(_output, nameof(I3_DamagedBytesInTheMiddleOfD_AreCaught));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        await bob.SaveAsync();
        await bob.CloseAsync();

        DamageAPageInTheMiddle(world.DatabasePath, world.ScratchDir);
        world.AdvanceTime(TimeSpan.FromMinutes(1));

        var violation = Only(world, "I3");
        Assert.Equal(ViolationKind.IntegrityCheckFailed, violation.Kind);
        Assert.Equal(Findings.NewFinding, violation.FindingId);
        world.Finish();
    }

    /// <summary>
    /// Overwrites the first 64 bytes of a table page from the middle of the file's page list with 0xFF, so the page's
    /// type byte is invalid. The page is found by reading a COPY's schema; D itself is only written to at the end.
    /// </summary>
    /// <param name="database">The database file to damage (must not be open).</param>
    /// <param name="scratchRoot">A scratch folder for the copy.</param>
    private static void DamageAPageInTheMiddle(string database, string scratchRoot)
    {
        var copy = Path.Combine(scratchRoot, "damage_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(database, copy);
        int pageSize;
        List<int> roots;
        using (var conn = new SqliteConnection($"Data Source={copy};Pooling=False"))
        {
            conn.Open();
            using var size = conn.CreateCommand();
            size.CommandText = "PRAGMA page_size";
            pageSize = Convert.ToInt32(size.ExecuteScalar());
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT rootpage FROM sqlite_master WHERE type='table' AND rootpage > 1 ORDER BY rootpage";
            roots = new List<int>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) roots.Add(reader.GetInt32(0));
        }
        SqliteConnection.ClearAllPools();
        File.Delete(copy);

        var root = roots[roots.Count / 2];
        using var stream = new FileStream(database, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = (long)(root - 1) * pageSize;
        stream.Write(Enumerable.Repeat((byte)0xFF, 64).ToArray());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I4: only the lock holder writes, onto the version it last saw
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// F12 is fixed. Bob saves. In the instant after his <c>D.tmp</c> has been verified, another person's lock appears on the
    /// share (the walk writes it from <see cref="World.AtCrashPoint"/>). Before this fix the save did not look at the lock
    /// again, the rename landed and I4 reported it. Now the save's final check, made immediately before the rename, finds the
    /// lock is not Bob's: his save is refused (LockLost), D is byte-for-byte what it was, and his edit stays in his kept working
    /// file with its marker. His D.tmp is left where it is: without the lock his save may not write to the share (rule 2), and the
    /// file's one shared name may by now be Alice's save in progress. I4 has nothing to report because there is no rename.
    /// </summary>
    [HarnessFact]
    public async Task I4_ALockThatChangesHandsBeforeTheRename_IsRefusedByTheFinalCheck_F12IsFixed()
    {
        using var world = new World(_output, nameof(I4_ALockThatChangesHandsBeforeTheRename_IsRefusedByTheFinalCheck_F12IsFixed));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        var databaseBefore = world.CurrentDatabaseHash();
        world.AtCrashPoint("Save.AfterTmpVerify", () => world.PlaceForeignLock("alice", "ALICE-PC"),
            "(after Bob's D.tmp is verified) Alice takes the lock");

        var outcome = await bob.SaveAsync();

        Assert.Equal(SaveOutcome.LockLost, outcome);
        Assert.Empty(world.Violations);                                    // I4 had no rename to look at
        Assert.Equal(databaseBefore, world.CurrentDatabaseHash());         // D was never touched
        Assert.Empty(world.EditsInDatabase());
        Assert.Contains("dept.db.tmp", world.ShareFiles());                // without the lock, the refused save leaves D.tmp alone
        Assert.True(bob.HasWorkingFile, "D' is kept");
        Assert.True(bob.HasDirtyMarker, "and so is its marker");
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());
        world.Finish();
    }

    /// <summary>
    /// What is left of F12 is still seen. The final check cannot close the few microseconds between itself and the rename (a plain
    /// file share has no compare-and-swap and no server), so invariant I4 is observed there, at <c>Save.AfterFinalCheck</c>, the true
    /// last moment before the rename. Another person's lock appears in exactly that window (the walk writes it from
    /// <see cref="World.AtCrashPoint"/>); the rename lands and I4 sees that the lock was not Bob's, classified F12. This proves the
    /// checker can still fail, and documents the accepted window.
    /// </summary>
    [HarnessFact]
    public async Task I4_ALockThatChangesHandsAfterTheFinalCheck_IsStillCaught_AsTheAcceptedWindow()
    {
        using var world = new World(_output, nameof(I4_ALockThatChangesHandsAfterTheFinalCheck_IsStillCaught_AsTheAcceptedWindow));
        world.ExpectFindings("F12");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.AtCrashPoint("Save.AfterFinalCheck", () => world.PlaceForeignLock("alice", "ALICE-PC"),
            "(the last moment before Bob's rename) Alice takes the lock");

        var outcome = await bob.SaveAsync();

        var violation = Only(world, "I4");
        Assert.Equal(ViolationKind.WriteWithoutLockOrOnStaleVersion, violation.Kind);
        Assert.Equal("F12", violation.FindingId);
        Assert.Equal("Save.AfterFinalCheck", violation.CrashPoint);
        Assert.Contains("alice", violation.Reason);

        // The rename really landed: Bob's version is now D, written while the lock file said Alice.
        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I2 / F1 (fixed): the X and an unconfirmed Discard no longer delete the only copy
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// F1 is fixed. Bob edits and crashes, restarts, and closes "Restore Unsaved Changes?" with the X. The X is not a decision, so
    /// the question is asked again; Bob then chooses Restore and bob-1 is back in his session. Before the fix the lifecycle took
    /// the X for Discard and deleted the kept changes at once (I2 fired and the classifier said F1). No violation; the story records
    /// "closed it with the X".
    /// </summary>
    [HarnessFact]
    public async Task I2_TheXOnTheRestoreQuestion_AsksAgain_AndNothingIsLost_F1IsFixed()
    {
        using var world = new World(_output, nameof(I2_TheXOnTheRestoreQuestion_AsksAgain_AndNothingIsLost_F1IsFixed));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.CrashComputer(bob);

        bob.Relaunch();
        bob.Prompts.ExpectChoice(HarnessTitles.RestoreUnsavedChanges, ChoiceAnswer.ClosedWithX)
                   .ExpectChoice(HarnessTitles.RestoreUnsavedChanges, ChoiceAnswer.Affirmative);
        await bob.OpenAsync();

        Assert.Equal(new[] { ChoiceAnswer.ClosedWithX, ChoiceAnswer.Affirmative }, bob.Prompts.Calls.Select(c => c.Answer!.Value));
        Assert.Empty(world.Violations);
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());   // the kept changes were restored
        Assert.Contains("closed it with the X", world.PrintStory(Array.Empty<string>()));
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// F1 is fixed. Discard on "Restore Unsaved Changes?" is no longer final: "Discard Unsaved Changes?" follows, and only its
    /// [Discard] deletes the kept changes. The confirmed discard is recorded for invariant I2 (the edit is nowhere, and that is the
    /// person's decision), so there is no violation; the edit is in <see cref="World.ConfirmedDiscards"/>.
    /// </summary>
    [HarnessFact]
    public async Task I2_DiscardOnTheRestoreQuestion_IsConfirmedFirst_AndThenItIsADecision_F1IsFixed()
    {
        using var world = new World(_output, nameof(I2_DiscardOnTheRestoreQuestion_IsConfirmedFirst_AndThenItIsADecision_F1IsFixed));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.CrashComputer(bob);

        bob.Relaunch();
        bob.Prompts.ExpectChoice(HarnessTitles.RestoreUnsavedChanges, ChoiceAnswer.Negative)
                   .ExpectChoice(HarnessTitles.DiscardUnsavedConfirm, ChoiceAnswer.Affirmative);
        await bob.OpenAsync();

        Assert.Empty(world.Violations);
        Assert.Contains("bob-1", world.ConfirmedDiscards);
        Assert.Empty(world.EditsInDatabase());
        Assert.Empty(bob.WorkingFileEdits());
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I5: the catalogue
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A message that talks about kept / restored / saved / safe work but is in no catalogue entry is itself a
    /// violation, so new wording cannot slip past; the known messages are classified.
    /// </summary>
    [HarnessFact]
    public void I5_AMessageNobodyClassified_IsAViolationButKnownOnesAreNot()
    {
        Assert.True(MessagePromises.Classify("Mystery", "Your work is safe with us.").IsUnclassifiedPromise);
        Assert.True(MessagePromises.Classify("", "Everything has been kept.").IsUnclassifiedPromise);

        Assert.Equal(new[] { PromiseKind.WillOfferRestore },
            MessagePromises.Classify("Unsaved Changes Kept", "... TermPoint will offer to restore them ...").Kinds);
        Assert.Contains(PromiseKind.DatabaseUnchanged, MessagePromises.Classify("", SavePrompt.VerifyFailedMessage).Kinds);
        Assert.Contains(PromiseKind.ChangesKept, MessagePromises.Classify("", SavePrompt.VerifyFailedMessage).Kinds);
        Assert.False(MessagePromises.Classify("Network Unreachable", "Cannot reach the database.").IsUnclassifiedPromise);
        Assert.Equal("bobs-version.db", MessagePromises.SavedFileName(SaveConflictPrompt.SavedMessage("bobs-version.db")));
    }

    /// <summary>
    /// The texts step 9.3d added to the catalogue are classified and are not "unclassified promises" any more: the heartbeat banner (a
    /// kept-changes promise, found by the first deep run) and three texts found by the sweep of every user-facing string in the save, lock,
    /// heartbeat, exit, switch, restore and recovery paths (each states a fact or admits a loss, and promises nothing to check). A plain
    /// test: it needs no world. The banners that are inline in the service are pinned here as the harness met them; the heartbeat one is also
    /// shown by the real service in <see cref="I5_TheHeartbeatBannerDuringAnOutage_IsAKeptPromise_AndItHolds"/>.
    /// </summary>
    [Fact]
    public void I5_TheTextsFoundByStep93d_AreInTheCatalogue()
    {
        const string heartbeat =
            "Network connection interrupted — your changes are safe locally but cannot be saved until the connection is restored.";
        var banner = MessagePromises.Classify("", heartbeat);
        Assert.False(banner.IsUnclassifiedPromise);
        Assert.Equal(new[] { PromiseKind.ChangesKept }, banner.Kinds);

        var missingFile = MessagePromises.Classify("", SavePrompt.SessionFileMissingMessage);
        Assert.False(missingFile.IsUnclassifiedPromise);
        Assert.Equal(new[] { PromiseKind.Info }, missingFile.Kinds);

        var noReconnect = MessagePromises.Classify("",
            "The database connection could not be restored after losing write access. Please close and reopen the database file.");
        Assert.False(noReconnect.IsUnclassifiedPromise);
        Assert.Equal(new[] { PromiseKind.Info }, noReconnect.Kinds);

        var newName = MessagePromises.Classify(RecoveryConflictPrompt.AlreadyExistsTitle, RecoveryConflictPrompt.AlreadyExistsMessage);
        Assert.False(newName.IsUnclassifiedPromise);
        Assert.Equal(new[] { PromiseKind.Info }, newName.Kinds);

        // A new message that talks about kept work and matches nothing is still caught (the catalogue did not become a catch-all).
        Assert.True(MessagePromises.Classify("", "Your edits are kept in a drawer.").IsUnclassifiedPromise);
    }

    /// <summary>
    /// The heartbeat banner is judged for truth (step 9.3d; its wording is finding F15, a wording finding, not a harness violation). Bob edits, the
    /// share becomes unreachable and three heartbeats fail: the real checkout service raises its "changes are safe locally" banner. It is a
    /// kept-changes promise, and it holds: bob-1 is still in his live session. Nothing is reported, and the first deep run's "unclassified
    /// promise" for exactly this story is gone.
    /// </summary>
    [HarnessFact]
    public async Task I5_TheHeartbeatBannerDuringAnOutage_IsAKeptPromise_AndItHolds()
    {
        using var world = new World(_output, nameof(I5_TheHeartbeatBannerDuringAnOutage_IsAKeptPromise_AndItHolds));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        // The banner is raised from a task the heartbeat failure starts (fire and forget, as in production), so wait for it, in real time,
        // with a limit; the funnel step that ran the ticks may end a moment before the banner is dispatched.
        var banners = new List<string>();
        var arrived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        bob.Checkout.SaveFailed += (text, _) => { lock (banners) banners.Add(text); arrived.TrySetResult(text); };   // what the window's save-error banner shows

        world.NetworkDown(bob);
        for (var tick = 0; tick < 3; tick++) await bob.HeartbeatTickAsync();
        Assert.Same(arrived.Task, await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(10))));
        world.AdvanceTime(TimeSpan.FromSeconds(1));   // the next funnel step: the promise the banner made is judged here

        string[] shown;
        lock (banners) shown = banners.ToArray();
        Assert.All(shown, text =>
        {
            var classified = MessagePromises.Classify("", text);
            Assert.False(classified.IsUnclassifiedPromise, text);
            Assert.Contains(PromiseKind.ChangesKept, classified.Kinds);
        });
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());     // the promise is true: the edit is on his disk
        Assert.Empty(world.Violations);
        world.Finish();
    }

    /// <summary>The title of the stale-lock question.</summary>
    private const string StaleLockTitle = "Database Lock Is Inactive";

    /// <summary>The title of the message a switch shows when the leaving database's changes could not be saved.</summary>
    private const string ChangesNotSavedTitle = "Changes Not Saved";

    /// <summary>
    /// Bob opens dept.db and edits; four minutes pass; Alice takes over his stale lock; Bob switches to other.db. His
    /// final save finds the lock gone, so he is told twice (the lock-lost banner and "Changes Not Saved") that TermPoint
    /// will offer to restore his changes next time he opens dept.db. Leaves Bob with other.db open and the
    /// promise pending, and Alice holding dept.db.
    /// </summary>
    /// <param name="world">The world (already created with dept.db).</param>
    /// <param name="bob">Bob.</param>
    /// <param name="alice">Alice.</param>
    private static async Task BobLeavesDeptAfterAliceTookOverAsync(World world, Computer bob, Computer alice)
    {
        await bob.OpenAsync();
        bob.Edit();
        world.AdvanceTime(TimeSpan.FromMinutes(4));

        alice.Prompts
            .ExpectChoice(StaleLockTitle, true)
            .ExpectStaleLockWait(true, before: () =>
                world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.StaleConfirmationSeconds + 1)));
        await alice.OpenAsync();

        bob.Prompts.ExpectMessage(ChangesNotSavedTitle);
        Assert.True(await bob.SwitchToAsync(world.EnsureOtherDatabase()));
        // Bob's mode on other.db is deliberately not asserted: the harness runs the lost-lock handler inline, so the demotion
        // his final save starts can land after the new checkout and flip it to read-only (see F16 in the design note and
        // CheckoutServiceTests.F16_...). The promise is about dept.db whichever way that race goes. Since step 9.3c-2b invariant I6
        // sees that race (a demotion that goes on against other.db, and a read-only session that holds other.db's lock), so the
        // tests that use this walk declare F16: here it is the harness form of the race, not the production form (the unit test).
    }

    /// <summary>
    /// A promise about dept.db is not judged at Bob's open of ANOTHER database. Production keeps unsaved changes per
    /// database (the working copy is named after the normalised source path), so opening other.db correctly offers
    /// nothing for dept.db. The review found the tracker reporting F6 here ("no restore was offered") although the
    /// promise was about dept.db. The promise must stay pending, and nothing may be reported.
    /// </summary>
    [HarnessFact]
    public async Task I5_ARestorePromisedForOneDatabase_IsNotJudgedAtTheOpenOfAnother()
    {
        using var world = new World(_output, nameof(I5_ARestorePromisedForOneDatabase_IsNotJudgedAtTheOpenOfAnother));
        world.ExpectFindings("F16");   // the promise is not broken, it is not yet due. F16: the harness-inline form of the switch race (see BobLeavesDeptAfterAliceTookOverAsync)
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");

        await BobLeavesDeptAfterAliceTookOverAsync(world, bob, alice);

        Assert.True(world.HasPendingPromise(bob), "the promise is about dept.db; opening other.db does not judge it");
        Assert.Equal("d", world.PendingPromiseCode(bob));
        Assert.All(world.Violations, v => Assert.Equal("I6", v.Invariant));   // nothing about the promise: only the inline-handler form of F16
        Assert.True(bob.HasDirtyMarker, "Bob's kept changes to dept.db are still on his disk");
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// One failure shows the same promise twice (the lock-lost banner, then "Changes Not Saved"): that is ONE claim, so
    /// it is held once and, if it turned out false, would be reported once (the review's smallest F6 story listed the
    /// identical violation twice). Pins that two messages made one pending promise.
    /// </summary>
    [HarnessFact]
    public async Task I5_TwoMessagesPromisingTheSameRestore_AreOnePromise()
    {
        using var world = new World(_output, nameof(I5_TwoMessagesPromisingTheSameRestore_AreOnePromise));
        world.ExpectFindings("F16");   // the harness-inline form of the switch race (see BobLeavesDeptAfterAliceTookOverAsync)
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");

        await BobLeavesDeptAfterAliceTookOverAsync(world, bob, alice);

        // Both messages really were shown to Bob ...
        Assert.Single(bob.Host.LockLossSurface.SaveErrors);
        Assert.Contains(bob.Prompts.Calls, c => c.Kind == PromptKind.Message && c.Title == ChangesNotSavedTitle);

        // ... and they are one pending promise, so the closing notes name it once.
        var pending = world.PendingPromiseLines();
        var line = Assert.Single(pending);
        Assert.Contains("dept.db", line);
        world.Finish();
    }

    /// <summary>
    /// The promise about dept.db IS judged at Bob's next edit-access open of dept.db: once Alice has closed, Bob opens
    /// dept.db, is offered his kept changes and chooses Restore, and today "Can't Restore" follows (Alice's close rewrote
    /// the file, F5). The promise is then false and reported ONCE (F6), at that open (not at the earlier open of
    /// other.db), although two messages made it. This is also a genuine F6 route: a promise judged at a later open of the
    /// same database where the restore cannot work.
    /// </summary>
    [HarnessFact]
    public async Task I5_ARestorePromisedForOneDatabase_IsJudgedAtTheNextOpenOfThatDatabase()
    {
        using var world = new World(_output, nameof(I5_ARestorePromisedForOneDatabase_IsJudgedAtTheNextOpenOfThatDatabase));
        world.ExpectFindings("F5", "F6", "F16");   // F16: the harness-inline form of the switch race (see BobLeavesDeptAfterAliceTookOverAsync)
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");

        await BobLeavesDeptAfterAliceTookOverAsync(world, bob, alice);
        Assert.True(world.HasPendingPromise(bob));

        await alice.CloseAsync();                       // Alice gives dept.db back, so Bob can get write access to it

        var export = Path.Combine(world.RootDir, "exports", "bob-recovered.db");
        Directory.CreateDirectory(Path.GetDirectoryName(export)!);
        bob.Prompts.ExpectChoice(HarnessTitles.RestoreUnsavedChanges, ChoiceAnswer.Affirmative)
                   .ExpectChoice(HarnessTitles.CantRestore, ChoiceAnswer.Affirmative)
                   .ExpectPick("Save Recovered Copy", export);
        Assert.True(await bob.SwitchToAsync(world.DatabasePath));

        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);
        Assert.False(world.HasPendingPromise(bob), "dept.db was opened for editing: the promise was judged");

        // Judged exactly once, at the open of dept.db (step 8), not at the open of other.db (step 6).
        var broken = Assert.Single(world.Violations, v => v.Kind == ViolationKind.BrokenPromise);
        Assert.Equal("F6", broken.FindingId);
        Assert.True(broken.Step > 6, "the promise is judged at the open of dept.db, after the open of other.db");
        Assert.Contains("Can't Restore", broken.Reason);
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// A broken "Your version was saved as X" promise is caught: the message names a file that does not exist when the
    /// step ends (the test deletes the export while the message is up), so the tracker reports the promise as false.
    /// </summary>
    [HarnessFact]
    public async Task I5_ASavedAsMessageForAFileThatIsNotThere_IsCaught()
    {
        using var world = new World(_output, nameof(I5_ASavedAsMessageForAFileThatIsNotThere_IsCaught));
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.ColleagueWritesDatabase("colleague-1");
        await bob.SaveAsync();                                     // refused: the conflict banner

        // Bob saves a copy, but the export lands in a file the test removes before the step ends: the message that follows
        // ("Your version was saved as ...") is then untrue by the end of the step.
        var export = Path.Combine(world.RootDir, "exports", "gone.db");
        Directory.CreateDirectory(Path.GetDirectoryName(export)!);
        bob.Prompts
            .ExpectPick("Save Recovered Copy", export)
            .ExpectWait(SaveConflictPrompt.SavingTitle, SaveConflictPrompt.SavingMessage)
            .ExpectMessage(SaveConflictPrompt.SavedTitle, SaveConflictPrompt.SavedMessage("gone.db"),
                before: () => File.Delete(export));
        await bob.ResolveConflictAsync(saveCopy: true);

        var broken = world.Violations.Where(v => v.Kind == ViolationKind.BrokenPromise).ToList();
        Assert.Contains(broken, v => v.Promise == PromiseKind.SavedAs);
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Power cuts at a named crash point (step 9.3c-2a)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A power cut at each of the save's eight crash points: Bob edits and presses Save, the power goes at the point, the
    /// disk is frozen and put back. D is then either the old version (every point before the rename) or the new one (the
    /// rename and after), never torn (I3), and bob-1 is still somewhere that survives (I2): in D, or in his kept working
    /// file with its marker. The marker is deleted only by the last step of the save. Every case runs through the normal
    /// funnel, so a breach of any invariant would throw instead of reaching these assertions.
    /// </summary>
    [HarnessFact]
    public async Task PowerCut_AtEachPointOfASave_LeavesDEitherOldOrNew_NeverTorn_AndEveryEditKept()
    {
        var savePoints = CrashPoints.All.Where(p => p.StartsWith("Save.", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, savePoints.Count);

        foreach (var point in savePoints)
        {
            using var world = new World(_output, $"a power cut at {point}");
            world.CreateDatabase();
            var bob = world.AddComputer("bob");
            await bob.OpenAsync();
            bob.Edit();

            var fired = await world.RunWithPowerCutAsync(bob, point, 1, "Bob presses Save", () => bob.SaveAsync());

            Assert.True(fired, $"the save never reached {point}");
            Assert.Null(world.IntegrityProblem(world.DatabasePath));                                   // I3: whole

            var renamed = point is "Save.AfterRename" or "Save.AfterMarkerDelete";
            Assert.Equal(renamed ? new[] { "bob-1" } : Array.Empty<string>(), world.EditsInDatabase());  // old or new, never in between
            Assert.True(world.LastLedger.IsSafe("bob-1"), $"bob-1 is nowhere after a cut at {point}");  // I2
            Assert.Equal(point != "Save.AfterMarkerDelete", bob.HasDirtyMarker);                       // only the last step deletes it
            Assert.False(bob.IsRunning, "the cut computer's process must be gone");
            Assert.Empty(world.Violations);
            world.Finish();
        }
    }

    /// <summary>
    /// What the program does after the cut is thrown away. A <c>finally</c> block around Bob's save deletes two files after
    /// the power went (one on the share, one on Bob's own disk); after the frozen disk is put back, both are still there, as
    /// they would be after a real power cut, and so is the verified <c>dept.db.tmp</c> the save had written at the cut.
    /// </summary>
    [HarnessFact]
    public async Task PowerCut_WhatAFinallyBlockDoesAfterTheCut_IsThrownAway()
    {
        using var world = new World(_output, nameof(PowerCut_WhatAFinallyBlockDoesAfterTheCut_IsThrownAway));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        var shareCanary = Path.Combine(world.ShareDir, "canary-on-the-share.txt");
        var localCanary = Path.Combine(bob.LocalDir, "canary-on-bobs-disk.txt");
        File.WriteAllText(shareCanary, "x");
        File.WriteAllText(localCanary, "x");

        var fired = await world.RunWithPowerCutAsync(bob, "Save.AfterTmpVerify", 1, "Bob presses Save", async () =>
        {
            try { await bob.SaveAsync(); }
            finally
            {
                File.Delete(shareCanary);   // runs after the cut, on the live disk ...
                File.Delete(localCanary);
            }
        });

        Assert.True(fired);
        Assert.True(File.Exists(shareCanary), "the file the finally block deleted on the share must be back");
        Assert.True(File.Exists(localCanary), "the file the finally block deleted on Bob's disk must be back");
        Assert.Contains("dept.db.tmp", world.ShareFiles());   // the save's verified temp file, as the cut left it
        Assert.Empty(world.EditsInDatabase());                // the rename never happened
        world.Finish();
    }

    /// <summary>
    /// The computer is dead after the cut: its next crash-point hit and its next network operation stop at once, and a
    /// second save by the dead process changes nothing, not even on the live disk before the frozen one is put back
    /// (production catches exceptions, so the first <see cref="PowerCutException"/> alone would not stop it). A restart
    /// gives a process with power again. The assertions run after the operation, because an assertion thrown inside it
    /// would be swallowed as "the dead process threw".
    /// </summary>
    [HarnessFact]
    public async Task PowerCut_TheComputerIsDeadAfterTheCut_UntilItIsRestarted()
    {
        using var world = new World(_output, nameof(PowerCut_TheComputerIsDeadAfterTheCut_UntilItIsRestarted));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        var deadAfterFirstSave = false;
        string? hashBefore = null, hashAfter = null;
        int hitsBefore = 0, hitsAfter = 0;
        Exception? hitThrew = null;
        NetworkFaultKind networkKind = NetworkFaultKind.None;

        var fired = await world.RunWithPowerCutAsync(bob, "Save.AfterLockCheck", 1, "Bob presses Save", async () =>
        {
            try { await bob.SaveAsync(); } catch (PowerCutException) { /* either way the process is dead now */ }
            deadAfterFirstSave = bob.PowerIsOut;

            hashBefore = world.CurrentDatabaseHash();
            hitsBefore = world.HitCount;
            try { await bob.SaveAsync(); } catch (PowerCutException) { /* stopped, as it should be */ }
            hashAfter = world.CurrentDatabaseHash();
            hitsAfter = world.HitCount;

            // The same two checks, asked directly as the dead process.
            world.EnterAction(bob.Life);
            try { CrashPoints.Hit("Save.AfterRename"); } catch (Exception ex) { hitThrew = ex; }
            networkKind = NetworkFaults.Consult("File.Exists", world.DatabasePath, null).Kind;
        });

        Assert.True(fired);
        Assert.True(deadAfterFirstSave);
        Assert.Equal(hashBefore, hashAfter);                  // the dead process wrote nothing to D
        Assert.Equal(hitsBefore, hitsAfter);                  // and got past no crash point
        Assert.IsType<PowerCutException>(hitThrew);
        Assert.Equal(NetworkFaultKind.Throw, networkKind);

        Assert.True(bob.PowerIsOut);
        bob.Relaunch();
        Assert.False(bob.PowerIsOut, "a restart is a new process, with power");
        Assert.Equal(SaveOutcome.Success, await RestartAndSaveAsync(bob));
        world.Finish();
    }

    /// <summary>
    /// The hook is process-wide, so it finds out which computer reached a crash point from a value that follows the flow of
    /// control (an <c>AsyncLocal</c>), not from "who is acting now". The value must survive every way production code hops
    /// threads: <c>Task.Run</c> (the lock release), the thread pool, a timer callback, a new thread, and an await that
    /// resumes on another thread. Each is checked here OUTSIDE any action, where the fallback ("the computer acting now") has
    /// no answer, so only the flowing value can attribute the hit to Bob.
    /// </summary>
    [HarnessFact]
    public async Task PowerCut_TheHookFindsTheComputer_OnEveryKindOfThread()
    {
        using var world = new World(_output, nameof(PowerCut_TheHookFindsTheComputer_OnEveryKindOfThread));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        world.EnterAction(bob.Life);   // Bob's action is running in this flow of control; nothing is "acting now" in the world
        Assert.Null(world.CurrentActor);

        var before = world.HitCount;
        await Task.Run(() => CrashPoints.Hit("Lock.AfterDeleted"));
        await Task.Yield();
        await Task.Run(async () => { await Task.Delay(5); CrashPoints.Hit("Close.AfterRelease"); });

        var pool = new ManualResetEventSlim();
        ThreadPool.QueueUserWorkItem(_ => { CrashPoints.Hit("Release.AfterLockReleased"); pool.Set(); });
        Assert.True(pool.Wait(TimeSpan.FromSeconds(10)));

        var timerDone = new ManualResetEventSlim();
        using var timer = new Timer(_ => { CrashPoints.Hit("Heartbeat.AfterTmpWritten"); timerDone.Set(); }, null, 1, Timeout.Infinite);
        Assert.True(timerDone.Wait(TimeSpan.FromSeconds(10)));

        var threadDone = new ManualResetEventSlim();
        new Thread(() => { CrashPoints.Hit("Switch.AfterRelease"); threadDone.Set(); }).Start();
        Assert.True(threadDone.Wait(TimeSpan.FromSeconds(10)));

        var hits = world.HitsSince(before);
        Assert.Equal(
            new[] { "Lock.AfterDeleted", "Close.AfterRelease", "Release.AfterLockReleased", "Heartbeat.AfterTmpWritten", "Switch.AfterRelease" }.OrderBy(p => p),
            hits.Select(h => h.Point).OrderBy(p => p));
        Assert.All(hits, h => Assert.Equal("bob", h.Computer));
        world.Finish();
    }

    /// <summary>Bob opens the database again (the kept changes are restored with the simplest answers) and saves.</summary>
    /// <param name="bob">Bob, restarted.</param>
    /// <returns>How the save ended.</returns>
    private static async Task<SaveOutcome> RestartAndSaveAsync(Computer bob)
    {
        bob.Prompts.ExpectChoice(HarnessTitles.RestoreUnsavedChanges, ChoiceAnswer.Affirmative);
        await bob.OpenAsync();
        return await bob.SaveAsync();
    }

    /// <summary>
    /// F8 is fixed. Bob saves, the power goes between the rename and the marker delete (<c>Save.AfterRename</c>). D already holds
    /// bob-1 and the marker is still there, and (the fix) the marker already carries the save's fingerprint, recorded before the
    /// rename. When Bob starts again he is offered "Restore Unsaved Changes?", chooses Restore, and the Restore WORKS: recovery
    /// recognises D as his own save. Before the fix he was told "Can't Restore" although nothing was missing. No violation; the
    /// story names the point.
    /// </summary>
    [HarnessFact]
    public async Task PowerCut_BetweenTheRenameAndTheMarkerDelete_RestoreWorks_F8IsFixed()
    {
        using var world = new World(_output, nameof(PowerCut_BetweenTheRenameAndTheMarkerDelete_RestoreWorks_F8IsFixed));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        Assert.True(await world.RunWithPowerCutAsync(bob, "Save.AfterRename", 1, "Bob presses Save", () => bob.SaveAsync()));
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());   // the save landed ...
        Assert.True(bob.HasDirtyMarker);                            // ... and the marker was not deleted
        Assert.Equal(world.CurrentDatabaseHash(), bob.PendingSaveHashInMarker());   // ... but it names D's new version

        bob.Relaunch();
        bob.Prompts.ExpectChoice(HarnessTitles.RestoreUnsavedChanges, ChoiceAnswer.Affirmative);   // and nothing else is asked
        await bob.OpenAsync();

        Assert.Empty(world.Violations);
        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);   // the Restore worked: Bob is writing again
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());     // with his edit in his session ...
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());    // ... and in D
        var story = world.PrintStory(Array.Empty<string>());
        Assert.Contains("Bob's computer lost power at Save.AfterRename", story);
        Assert.DoesNotContain("Can't Restore", story);
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// The key of a power-cut event reads back into the same event, and a key that is not one, or names an event that does
    /// not exist, is not parsed. (A plain unit test: it runs with the normal suite.)
    /// </summary>
    [Fact]
    public void PowerCutEvent_KeyRoundTrips()
    {
        var known = EventCatalogue.Build().Concat(SweepEvents.Build()).ToDictionary(e => e.Key);

        foreach (var key in new[]
                 {
                     "PowerCut(Save(bob) at Save.AfterRename)",
                     "PowerCut(Sleep(alice,10m) at Demote.AfterLockReleased #2)",
                     "PowerCut(Refresh(bob) at Refresh.AfterSwap)",
                 })
        {
            var parsed = PowerCutEvent.TryParse(key, known);
            Assert.NotNull(parsed);
            Assert.Equal(key, parsed!.Key);
        }

        Assert.Null(PowerCutEvent.TryParse("Save(bob)", known));
        Assert.Null(PowerCutEvent.TryParse("PowerCut(NoSuchEvent(bob) at Save.AfterRename)", known));
        Assert.Null(PowerCutEvent.TryParse("PowerCut(AdvanceTime(4m) at Save.AfterRename)", known));   // not a computer's event
    }
}
