using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="NewFilePlacement"/> — the "put a finished local file at a path that must not exist yet"
/// pipeline shared by <see cref="NewDatabaseFile"/> and <see cref="DatabaseRestore.CreateFromBackupAsync"/> (spec item
/// 5c). The end-to-end behaviour of both callers is covered by their own tests; what is proved HERE is the helper's
/// own contract, including the two ways a file can appear at the destination while the work is under way, and the
/// <c>awaitTmpCleanup</c> promise: with it, a failed placement has removed its target.tmp by the time it returns.
/// </summary>
public sealed class NewFilePlacementTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Stands in for the shared folder.</summary>
    private readonly string _shareDir;

    /// <summary>Stands in for the local scratch folder holding the finished file.</summary>
    private readonly string _localDir;

    /// <summary>Creates isolated folders for this test instance.</summary>
    public NewFilePlacementTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir = Path.Combine(Path.GetTempPath(), $"nfp_share_{id}");
        _localDir = Path.Combine(Path.GetTempPath(), $"nfp_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_localDir);
    }

    /// <summary>Removes both folders (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_shareDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_localDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The bytes of the "finished" local file. Any bytes will do: the helper never opens the file with SQLite.</summary>
    private static readonly byte[] FileBytes = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();

    /// <summary>Creates the finished local file and returns its path.</summary>
    private string LocalFile()
    {
        var path = Path.Combine(_localDir, "finished.db");
        File.WriteAllBytes(path, FileBytes);
        return path;
    }

    /// <summary>The destination path inside the share.</summary>
    private string Target(string name = "new.db") => Path.Combine(_shareDir, name);

    /// <summary>Entries in the share folder, sorted, for exact "nothing else is there" assertions.</summary>
    private string[] ShareEntries() => Directory.GetFileSystemEntries(_shareDir).OrderBy(e => e, StringComparer.Ordinal).ToArray();

    // ── Tmp path ──────────────────────────────────────────────────────────────

    /// <summary>The tmp sibling is the target plus ".tmp" — the name the orphan sweep looks for.</summary>
    [Fact]
    public void TmpPathFor_IsTheTargetPlusTmp()
    {
        Assert.Equal(Target() + ".tmp", NewFilePlacement.TmpPathFor(Target()));
    }

    // ── Folder ────────────────────────────────────────────────────────────────

    /// <summary>A missing folder (several levels deep) is created; an existing one is left alone.</summary>
    [Fact]
    public async Task EnsureFolderAsync_CreatesAMissingFolder_AndAcceptsAnExistingOne()
    {
        var deep = Path.Combine(_shareDir, "a", "b", "c", "new.db");

        Assert.Null(await NewFilePlacement.EnsureFolderAsync(deep));
        Assert.True(Directory.Exists(Path.GetDirectoryName(deep)));
        Assert.Null(await NewFilePlacement.EnsureFolderAsync(deep));
    }

    // ── Refusal ───────────────────────────────────────────────────────────────

    /// <summary>An absent target passes the probe; a present one is AlreadyExists.</summary>
    [Fact]
    public async Task RefuseIfPresentAsync_AbsentPasses_PresentIsAlreadyExists()
    {
        var target = Target();

        Assert.Null(await NewFilePlacement.RefuseIfPresentAsync(target));

        File.WriteAllText(target, "there");
        var refused = await NewFilePlacement.RefuseIfPresentAsync(target);

        Assert.NotNull(refused);
        Assert.Equal(NewDatabaseOutcome.AlreadyExists, refused!.Outcome);
    }

    // ── Placement ─────────────────────────────────────────────────────────────

    /// <summary>The happy path: Created, the target holds exactly the local file's bytes, no .tmp, the local file is left alone.</summary>
    [Fact]
    public async Task PlaceAsync_Success_CreatesTheTarget_WithNoTmpLeftBehind()
    {
        var local = LocalFile();
        var target = Target();

        var result = await NewFilePlacement.PlaceAsync(local, target, awaitTmpCleanup: true);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.Equal(FileBytes, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.Equal(FileBytes, File.ReadAllBytes(local));
    }

    /// <summary>A target that is already there is refused at the late probe and not touched; no .tmp is written at all.</summary>
    [Fact]
    public async Task PlaceAsync_TargetExists_IsAlreadyExists_AndTheTargetIsUntouched()
    {
        var local = LocalFile();
        var target = Target();
        File.WriteAllText(target, "someone else's database");

        var result = await NewFilePlacement.PlaceAsync(local, target, awaitTmpCleanup: true);

        Assert.Equal(NewDatabaseOutcome.AlreadyExists, result.Outcome);
        Assert.Equal("someone else's database", File.ReadAllText(target));
        Assert.Equal(new[] { target }, ShareEntries());
    }

    /// <summary>
    /// The target appears WHILE the transfer to .tmp is under way — after the late probe, so only the no-overwrite
    /// move can stop it. The move refuses, the result says AlreadyExists, the newcomer's file is untouched and our
    /// .tmp is gone.
    /// </summary>
    [Fact]
    public async Task PlaceAsync_TargetAppearsDuringTheCopy_IsAlreadyExists_NewcomerUntouched_TmpRemoved()
    {
        var local = LocalFile();
        var target = Target();
        var newcomer = new byte[] { 9, 9, 9 };

        var result = await NewFilePlacement.PlaceAsync(
            local, target, awaitTmpCleanup: true,
            copyAsync: async (source, destination) =>
            {
                var done = await NetworkFileOps.CopyAsync(source, destination);
                File.WriteAllBytes(target, newcomer);   // the other user creates the same name, mid-transfer
                return done;
            });

        Assert.Equal(NewDatabaseOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(newcomer, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
    }

    /// <summary>A stalled transfer is Unreachable; with awaitTmpCleanup the partial .tmp it left is gone on return.</summary>
    [Fact]
    public async Task PlaceAsync_StalledCopy_IsUnreachable_AndTheAwaitedCleanupRemovesTheTmp()
    {
        var local = LocalFile();
        var target = Target();

        var result = await NewFilePlacement.PlaceAsync(
            local, target, awaitTmpCleanup: true,
            copyAsync: (source, destination) =>
            {
                File.WriteAllBytes(destination, FileBytes.AsSpan(0, 100).ToArray());   // the debris of a severed transfer
                return Task.FromResult(false);
            });

        Assert.Equal(NewDatabaseOutcome.Unreachable, result.Outcome);
        Assert.Empty(ShareEntries());
    }

    /// <summary>Bytes that arrive different from what was sent are a Failed placement, never moved into place; the .tmp is removed.</summary>
    [Fact]
    public async Task PlaceAsync_TornCopy_IsFailed_AndNothingIsMovedIntoPlace()
    {
        var local = LocalFile();
        var target = Target();

        var result = await NewFilePlacement.PlaceAsync(
            local, target, awaitTmpCleanup: true,
            copyAsync: (source, destination) =>
            {
                File.WriteAllBytes(destination, FileBytes.Reverse().ToArray());   // arrives wrong
                return Task.FromResult(true);
            });

        Assert.Equal(NewDatabaseOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Empty(ShareEntries());
    }

    /// <summary>An exception from the transfer propagates to the caller — after the .tmp has been removed.</summary>
    [Fact]
    public async Task PlaceAsync_ACopyThatThrows_Propagates_AfterRemovingTheTmp()
    {
        var local = LocalFile();
        var target = Target();

        await Assert.ThrowsAsync<IOException>(() => NewFilePlacement.PlaceAsync(
            local, target, awaitTmpCleanup: true,
            copyAsync: (source, destination) =>
            {
                File.WriteAllBytes(destination, [1, 2, 3]);
                throw new IOException("the share went away");
            }));

        Assert.Empty(ShareEntries());
    }

    /// <summary>A hash the caller already has is used as is: a wrong one makes the verification fail even though the copy is faithful.</summary>
    [Fact]
    public async Task PlaceAsync_UsesTheLocalHashItIsGiven()
    {
        var local = LocalFile();
        var target = Target();

        var result = await NewFilePlacement.PlaceAsync(local, target, awaitTmpCleanup: true, localHash: new string('0', 64));

        Assert.Equal(NewDatabaseOutcome.Failed, result.Outcome);
        Assert.Empty(ShareEntries());
    }
}
