using System.Security.Cryptography;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for 1.2.4 item 4.1 (every save uploads to a temporary file of its own, <c>dept.db.&lt;8 hex&gt;.tmp</c>), run with the
/// pause machinery on a real two-computer world. They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// <list type="bullet">
/// <item><description><b>The field case</b> (<see cref="Field_AFrozenWritersTempFileHeldOpen_DoesNotBlockTheNextWriter_HerSaveSucceedsAtOnce"/>):
/// Bob's computer is frozen halfway through a save and the server keeps his temporary file open. Before 1.2.4 item 4.1 Alice's every save
/// failed ("another program is using the database") because her upload used the same name.</description></item>
/// <item><description><b>The worse case</b> (<see cref="Worse_ABobWhoLostTheLockResumesDuringAlicesUpload_TouchesOnlyHisOwnTempFile"/>):
/// Bob's save was held before its upload; Alice takes over and is held after her own upload; Bob wakes and his save goes on. Before the
/// item his save deleted Alice's file (the shared name). Now he touches only his own.</description></item>
/// </list>
/// </summary>
public sealed class OwnTempFileProofTests
{
    /// <summary>The title of the stale-lock question Alice is asked when she takes over.</summary>
    private const string StaleLockTitle = "Database Lock Is Inactive";

    /// <summary>The longest any hold may last in these tests before the test fails instead of hanging.</summary>
    private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(30);

    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public OwnTempFileProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>Scripts Alice's answers for taking over Bob's stale lock (take over, then the confirmation wait runs on the fake clock).</summary>
    /// <param name="world">The world whose clock the wait advances.</param>
    /// <param name="alice">The computer that takes over.</param>
    private static void ScriptTakeOver(World world, Computer alice)
        => alice.Prompts
            .ExpectChoice(StaleLockTitle, true)
            .ExpectStaleLockWait(true, before: () =>
                world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.StaleConfirmationSeconds + 1)));

    /// <summary>SHA-256 of a file, read with sharing so it works while a test holds the file open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash as hex.</returns>
    private static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// The field case. Bob presses Save; after his temporary file is uploaded his computer freezes (the save is held) and the server keeps
    /// the file open against everyone. Alice takes over the stale lock, edits and saves. Her sweep at checkout meets the frozen handle and
    /// cannot delete Bob's file (it is logged), but her save uploads to a temporary file of its own, so it succeeds at once: no banner, D holds
    /// her edit. Bob's computer wakes; the handle is let go; his save is refused at the final check (the lock is Alice's), deletes only his own
    /// file, and his edit stays in his kept working file with its marker.
    /// </summary>
    [HarnessFact]
    public async Task Field_AFrozenWritersTempFileHeldOpen_DoesNotBlockTheNextWriter_HerSaveSucceedsAtOnce()
    {
        using var world = new World(_output, nameof(Field_AFrozenWritersTempFileHeldOpen_DoesNotBlockTheNextWriter_HerSaveSucceedsAtOnce));
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");
        await bob.OpenAsync();
        bob.Edit();

        Task<SaveOutcome>? bobSave = null;
        SaveOutcome? aliceOutcome  = null;
        var heldWhileAliceSaved    = false;

        var result = await world.RunWithPauseAsync(bob, "Save.AfterTmpCopy", 1, "Bob presses Save",
            operation: () => bobSave = bob.SaveAsync(),
            eventStory: "the server keeps Bob's temporary file open and Alice takes over and saves",
            eventIsPossible: () => true,
            pauseEvent: async () =>
            {
                world.OtherProgramOpens(OtherProgramFile.SaveTemp, OtherProgramShare.None);   // the server's handle on Bob's half-finished upload
                world.AdvanceTime(TimeSpan.FromMinutes(5));                                   // Bob's lock is stale
                ScriptTakeOver(world, alice);
                await alice.OpenAsync();
                alice.Edit();
                aliceOutcome = await alice.SaveAsync();
                heldWhileAliceSaved = world.OtherProgramHolding is not null;                   // still held while her save went through
                world.OtherProgramReleases();                                                 // Bob's computer wakes; the server lets go
            });

        Assert.Equal(PauseOutcome.Interleaved, result.Outcome);
        Assert.True(heldWhileAliceSaved, "Bob's temporary file must have been held open while Alice saved");
        Assert.Equal(SaveOutcome.Success, aliceOutcome);                       // at once, with the frozen handle still there
        Assert.Contains(alice.Logger.Infos, l => l.Contains("could not delete orphaned tmp"));   // her sweep did meet the frozen handle
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());            // D has her change, and not Bob's
        Assert.Equal(SaveOutcome.LockLost, await bobSave!);                    // Bob's late save is refused
        Assert.Null(world.CurrentSaveTempFile());                              // and no temporary file of anybody's is left
        Assert.True(bob.HasWorkingFile && bob.HasDirtyMarker, "Bob's work is kept with its marker");
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());
        Assert.Empty(world.Violations);
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// The worse case. Bob presses Save and his save is held after the first checks, before its upload (his computer sleeps). Alice takes
    /// over the stale lock, edits, saves, and her save is held after its own temporary file is uploaded. Bob's computer wakes and his save goes
    /// on: it uploads and verifies its own file, the final check finds the lock is Alice's, and it deletes ITS OWN file only. Alice's file is
    /// still on the share, byte for byte, and D is untouched. Alice's save then completes: D has her edit and no temporary file is left.
    /// Bob's edit stays in his kept working file with its marker. (Before the item both used <c>dept.db.tmp</c>: Bob's delete removed
    /// Alice's file and her save failed.)
    /// </summary>
    [HarnessFact]
    public async Task Worse_ABobWhoLostTheLockResumesDuringAlicesUpload_TouchesOnlyHisOwnTempFile()
    {
        using var world = new World(_output, nameof(Worse_ABobWhoLostTheLockResumesDuringAlicesUpload_TouchesOnlyHisOwnTempFile));
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");
        await bob.OpenAsync();
        bob.Edit();
        var databaseBefore = world.CurrentDatabaseHash();

        using var bobAtSnapshot  = new ManualResetEventSlim();
        using var bobGoes        = new ManualResetEventSlim();
        using var aliceAfterCopy = new ManualResetEventSlim();
        using var aliceGoes      = new ManualResetEventSlim();
        string? aliceTmp = null, aliceTmpHash = null;
        SaveOutcome bobOutcome = default, aliceOutcome = default;
        var aliceFileUntouched = false;

        // The whole story is one funnel step, so the overlapping actions nest in it and the invariants run once, at the end.
        await world.DoAsync(null, "Bob's save is held before its upload, Alice takes over and is held after her upload, then Bob's save goes on", async () =>
        {
            // 1. Bob presses Save; his save is held right after its snapshot, before anything is uploaded.
            world.AtCrashPoint("Save.AfterLocalSnapshot", () => { bobAtSnapshot.Set(); bobGoes.Wait(HoldLimit); },
                "(after Bob's snapshot, before his upload) Bob's computer sleeps");
            var bobSave = Task.Run(() => bob.SaveAsync());
            Assert.True(bobAtSnapshot.Wait(HoldLimit), "Bob's save should have reached its snapshot");

            // 2. Alice takes over his stale lock, edits and saves; her save is held after her own upload.
            world.AdvanceTime(TimeSpan.FromMinutes(5));
            ScriptTakeOver(world, alice);
            await alice.OpenAsync();
            alice.Edit();
            world.AtCrashPoint("Save.AfterTmpCopy", () => { aliceAfterCopy.Set(); aliceGoes.Wait(HoldLimit); },
                "(after Alice's upload) Alice's save is held");
            var aliceSave = Task.Run(() => alice.SaveAsync());
            Assert.True(aliceAfterCopy.Wait(HoldLimit), "Alice's save should have uploaded its temporary file");
            aliceTmp     = ShareTemp.Single(world.DatabasePath);              // the only temporary file on the share: Alice's
            aliceTmpHash = HashOf(aliceTmp);

            // 3. Bob's computer wakes and his save goes on while Alice's file sits on the share.
            bobGoes.Set();
            bobOutcome = await bobSave;
            aliceFileUntouched = ShareTemp.Leftovers(world.DatabasePath).SequenceEqual(new[] { aliceTmp })
                                 && HashOf(aliceTmp) == aliceTmpHash
                                 && world.CurrentDatabaseHash() == databaseBefore;

            // 4. Alice's save goes on and completes.
            aliceGoes.Set();
            aliceOutcome = await aliceSave;
            return 0;
        });

        Assert.Equal(SaveOutcome.LockLost, bobOutcome);                       // Bob's final check refused it
        Assert.True(aliceFileUntouched, "after Bob's whole save, Alice's file is the only temporary file left, byte for byte, and D is untouched");
        Assert.Equal(SaveOutcome.Success, aliceOutcome);                      // Alice's file survived Bob's whole save
        Assert.Equal(new[] { "alice-1" }, world.EditsInDatabase());           // D has her change and not Bob's
        Assert.NotEqual(databaseBefore, world.CurrentDatabaseHash());
        Assert.Null(world.CurrentSaveTempFile());                             // her rename consumed her file; his own was deleted
        Assert.NotNull(aliceTmpHash);
        Assert.True(bob.HasWorkingFile && bob.HasDirtyMarker, "Bob's work is kept with its marker");
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());
        Assert.Empty(world.Violations);
        world.AssertAllScriptsConsumed();
        world.Finish();
    }
}
