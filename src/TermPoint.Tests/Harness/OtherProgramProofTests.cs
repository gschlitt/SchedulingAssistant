using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for the "another program holds a file on the share" event (spec item 8.3): the handle is a real one that production meets, the harness's own
/// observers are not locked out by it (they read through it), and a power cut still freezes and restores the disk while a file is held. They run only with
/// <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// </summary>
public sealed class OtherProgramProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public OtherProgramProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A program that holds the shared database with <see cref="FileShare.None"/> really keeps everyone else out (a plain open of the file fails with a sharing
    /// violation), yet the harness reads the file through the handle: the ledger, the edits and the integrity check still work, so no invariant reports the
    /// database as unreadable or damaged.
    /// </summary>
    [HarnessFact]
    public async Task AFileHeldWithoutSharing_IsReallyLocked_ButTheHarnessCanStillReadIt()
    {
        using var world = new World(_output, nameof(AFileHeldWithoutSharing_IsReallyLocked_ButTheHarnessCanStillReadIt));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();
        Assert.Equal(SaveOutcome.Success, await bob.SaveAsync());

        world.OtherProgramOpens(OtherProgramFile.Database, OtherProgramShare.None);

        Assert.NotNull(world.OtherProgramHolding);
        Assert.Throws<IOException>(() =>
        {
            using var _ = new FileStream(world.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        });
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());      // read through the handle
        Assert.Null(world.IntegrityProblem(world.DatabasePath));

        world.OtherProgramReleases();
        Assert.Null(world.OtherProgramHolding);
        using (new FileStream(world.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None)) { }   // free again
        await bob.CloseAsync();
        world.Finish();
    }

    /// <summary>
    /// The disk freeze of a power cut copies THROUGH a file another program holds without sharing, and putting the frozen disk back needs the file free, so the
    /// other program lets go at the cut (the story says so). Bob's save is cut at <c>Save.AfterLockCheck</c> while the program holds D with
    /// <see cref="FileShare.None"/>: no exception from the harness, the handle is gone and D is as it was.
    /// </summary>
    [HarnessFact]
    public async Task APowerCut_WhileAFileIsHeldWithoutSharing_StillFreezesAndRestoresTheDisk()
    {
        using var world = new World(_output, nameof(APowerCut_WhileAFileIsHeldWithoutSharing_StillFreezesAndRestoresTheDisk));
        world.ExpectFindings(KnownFindings.Ids.ToArray());
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        world.OtherProgramOpens(OtherProgramFile.Database, OtherProgramShare.None);
        var cut = await world.RunWithPowerCutAsync(bob, "Save.AfterLockCheck", 1, "Bob presses Save", () => bob.SaveAsync());

        Assert.True(cut, "the save reaches its first crash point (the lock check) even with D held, so the power is cut there");
        Assert.Null(world.OtherProgramHolding);
    }

    /// <summary>The keys of the new events and pause actions read back (a pause key with the action parses).</summary>
    [Fact]
    public void TheKeysOfTheNewEvents_AreUniqueAndParse()
    {
        var keys = EventCatalogue.AllKeys();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains("OtherProgramOpens(lockfile,none)", keys);
        Assert.Contains("OtherProgramReleases", keys);

        var known = EventCatalogue.Build().Concat(SweepEvents.Build()).ToDictionary(e => e.Key);
        var parsed = PauseEvent.TryParse("Pause(Save(bob) at Save.AfterTmpCopy; OtherProgramOpensSaveTempReadWriteDelete)", known);
        Assert.NotNull(parsed);
        Assert.Equal("Pause(Save(bob) at Save.AfterTmpCopy; OtherProgramOpensSaveTempReadWriteDelete)", parsed!.Key);
    }
}
