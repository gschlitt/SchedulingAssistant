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
    /// Bob saves. In the instant after his <c>D.tmp</c> has been verified and just before the rename, another person's
    /// lock appears on the share (the walk writes it from <see cref="World.AtCrashPoint"/>). The save does not look at
    /// the lock again before step 5, so the rename still lands: I4 sees that the lock was not Bob's at the rename, and the
    /// finding is F12 (the review's "the save doesn't re-check the lock just before the rename"). This is the
    /// theoretical F12 made real: the test also asserts that Bob's save did overwrite D.
    /// </summary>
    [HarnessFact]
    public async Task I4_ALockThatChangesHandsBeforeTheRename_IsCaught_AsF12()
    {
        using var world = new World(_output, nameof(I4_ALockThatChangesHandsBeforeTheRename_IsCaught_AsF12));
        world.ExpectFindings("F12");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.AtCrashPoint("Save.AfterTmpVerify", () => world.PlaceForeignLock("alice", "ALICE-PC"),
            "(just before Bob's rename) Alice takes the lock");

        var outcome = await bob.SaveAsync();

        var violation = Only(world, "I4");
        Assert.Equal(ViolationKind.WriteWithoutLockOrOnStaleVersion, violation.Kind);
        Assert.Equal("F12", violation.FindingId);
        Assert.Equal("Save.AfterTmpVerify", violation.CrashPoint);
        Assert.Contains("alice", violation.Reason);

        // The rename really landed: Bob's version is now D, written while the lock file said Alice.
        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        world.Finish();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // I5 / F1: the X on "Restore Unsaved Changes?" deletes the only copy
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bob edits and crashes, restarts, and closes "Restore Unsaved Changes?" with the X. The lifecycle takes the X for
    /// Discard and deletes the kept changes with no confirmation: bob-1 is nowhere. I2 fires and the classifier says
    /// F1. The story records "closed it with the X", which is how the classifier tells a dismissal from a decision.
    /// </summary>
    [HarnessFact]
    public async Task I5_TheXOnTheRestoreQuestion_LosesTheWork_AsF1()
        => await LoseWorkByAnsweringTheRestoreQuestion(ChoiceAnswer.ClosedWithX, nameof(I5_TheXOnTheRestoreQuestion_LosesTheWork_AsF1));

    /// <summary>
    /// The same loss through the Discard button of "Restore Unsaved Changes?": it is not confirmed either today, so it is
    /// F1 as well.
    /// </summary>
    [HarnessFact]
    public async Task I5_DiscardOnTheRestoreQuestion_IsNotConfirmed_AlsoF1()
        => await LoseWorkByAnsweringTheRestoreQuestion(ChoiceAnswer.Negative, nameof(I5_DiscardOnTheRestoreQuestion_IsNotConfirmed_AlsoF1));

    /// <summary>The shared body of the two F1 proofs.</summary>
    /// <param name="answer">How Bob answers "Restore Unsaved Changes?".</param>
    /// <param name="title">The story's title.</param>
    private async Task LoseWorkByAnsweringTheRestoreQuestion(ChoiceAnswer answer, string title)
    {
        using var world = new World(_output, title);
        world.ExpectFindings("F1");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.CrashComputer(bob);

        bob.Relaunch();
        bob.Prompts.ExpectChoice(HarnessTitles.RestoreUnsavedChanges, answer);
        await bob.OpenAsync();

        var call = Assert.Single(bob.Prompts.Calls);
        Assert.Equal(answer, call.Answer);

        var violation = Only(world, "I2");
        Assert.Equal("bob-1", violation.Edit);
        Assert.Equal("F1", violation.FindingId);
        Assert.Empty(world.EditsInDatabase());
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
}
