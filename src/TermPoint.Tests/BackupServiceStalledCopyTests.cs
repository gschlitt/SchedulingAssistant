using Microsoft.Data.Sqlite;
using Moq;
using TermPoint.Data;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 30: when the copy of a backup to the backup folder stalls (the folder stopped responding), the partial
/// file is removed through <see cref="NetworkFileOps.DeleteAsync"/>, under the same network deadline as the copy, not
/// with a plain <c>File.Exists</c> / <c>File.Delete</c> that could hold the calling thread (the UI thread, when the
/// backup runs at lock acquisition or from the Save and Backup panel) for the full SMB timeout.
///
/// <para>The tests drive the real <see cref="BackupService.PerformBackupAsync"/> against a local working copy and a
/// backup folder playing the share. The network fault injector (<see cref="NetworkFaults"/>) makes the copy into the
/// backup folder report a stall. Going through <see cref="NetworkFileOps"/> is proved by the injector being consulted
/// for the delete: a plain <c>File.Delete</c> never asks it. Both static hooks and the backup-folder setting are
/// restored in <see cref="Dispose"/>.</para>
/// </summary>
public sealed class BackupServiceStalledCopyTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Local folder holding the working copy that is backed up.</summary>
    private readonly string _localDir;

    /// <summary>Folder playing the network backup folder.</summary>
    private readonly string _backupDir;

    /// <summary>The backup-folder setting before the test, put back in <see cref="Dispose"/>.</summary>
    private readonly string? _savedBackupFolder;

    /// <summary>Every operation the injector was asked about, in order.</summary>
    private readonly List<NetworkFaultRequest> _requests = new();

    /// <summary>Creates the two folders, a healthy working copy, and points the backup-folder setting at the "share".</summary>
    public BackupServiceStalledCopyTests()
    {
        var id     = Guid.NewGuid().ToString("N");
        _localDir  = Path.Combine(Path.GetTempPath(), $"bsc_local_{id}");
        _backupDir = Path.Combine(Path.GetTempPath(), $"bsc_share_{id}");
        Directory.CreateDirectory(_localDir);
        Directory.CreateDirectory(_backupDir);
        DbFileTestHelpers.CreateHealthyDatabase(WorkingCopy);

        _savedBackupFolder = AppSettings.Current.BackupFolderPath;
        AppSettings.Current.BackupFolderPath = _backupDir;
    }

    /// <summary>Resets the injector and the setting, and deletes both folders (best effort).</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        AppSettings.Current.BackupFolderPath = _savedBackupFolder;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_localDir,  recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_backupDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The working copy (D') the backup snapshots.</summary>
    private string WorkingCopy => Path.Combine(_localDir, "working.db");

    /// <summary>True when <paramref name="path"/> is inside the backup folder.</summary>
    /// <param name="path">A path, or null.</param>
    private bool InBackupFolder(string? path)
        => path is not null && path.StartsWith(_backupDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Installs a fault decision that records every request, makes the copy into the backup folder report a stall
    /// after it has run (so a full partial file is left behind, as a real stall leaves bytes), and applies
    /// <paramref name="deleteFault"/> to a delete in the backup folder. Everything else runs normally.
    /// </summary>
    /// <param name="deleteFault">What a delete in the backup folder does.</param>
    private void StallTheCopy(NetworkFaultDecision deleteFault)
        => NetworkFaults.Decide = r =>
        {
            lock (_requests) _requests.Add(r);
            if (r.Label == "CopyFile" && InBackupFolder(r.DestinationPath)) return NetworkFaultDecision.TimeOutAfterRunning;
            if (r.Label == "File.Delete" && InBackupFolder(r.Path))        return deleteFault;
            return NetworkFaultDecision.None;
        };

    /// <summary>
    /// A backup service over the working copy. Only what a stalled backup reaches is real: the database context's
    /// path and the semester context (for the CSV name). The repositories are never reached on this path.
    /// </summary>
    /// <param name="logger">The logger to record into.</param>
    private BackupService CreateService(IAppLogger logger)
    {
        var db = new Mock<IDatabaseContext>();
        db.SetupGet(d => d.DatabasePath).Returns(WorkingCopy);
        return new BackupService(db.Object, new SemesterContext(), null!, null!, null!, null!, null!, null!, null!, logger);
    }

    /// <summary>The .db files in the backup folder.</summary>
    private string[] BackupFiles() => Directory.GetFiles(_backupDir, "*.db");

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The copy stalls: the backup fails with "did not respond", the partial file is gone, and it was deleted through
    /// the deadline-bounded <see cref="NetworkFileOps.DeleteAsync"/> (the injector was asked about that delete). The
    /// local snapshot is cleaned up as before.
    /// </summary>
    [Fact]
    public async Task StalledCopy_RemovesThePartialFile_ThroughTheDeadlineBoundedDelete()
    {
        StallTheCopy(NetworkFaultDecision.None);
        var logger = new RecordingAppLogger();

        var result = await CreateService(logger).PerformBackupAsync();

        Assert.False(result.Success);
        Assert.Contains("did not respond", result.ErrorMessage);
        Assert.Empty(BackupFiles());
        Assert.Contains(_requests, r => r.Label == "File.Delete" && InBackupFolder(r.Path) && r.Path!.EndsWith(".db"));
        Assert.DoesNotContain(logger.Infos, line => line.Contains("could not be removed in time"));
        Assert.False(File.Exists(WorkingCopy + ".backuptmp"));
    }

    /// <summary>
    /// The copy stalls and the folder is still dead when the partial file is deleted: the backup still returns its
    /// "did not respond" failure (nothing throws, nothing waits beyond the delete's deadline), the partial file stays
    /// for rotation to remove, and one local log line says so.
    /// </summary>
    [Fact]
    public async Task StalledCopy_WhenTheDeleteAlsoTimesOut_ReturnsTheFailure_AndLeavesTheFileForRotation()
    {
        StallTheCopy(NetworkFaultDecision.TimeOut);
        var logger = new RecordingAppLogger();

        var result = await CreateService(logger).PerformBackupAsync();

        Assert.False(result.Success);
        Assert.Contains("did not respond", result.ErrorMessage);
        Assert.Single(BackupFiles());
        Assert.Contains(logger.Infos, line => line.Contains("could not be removed in time"));
        Assert.False(File.Exists(WorkingCopy + ".backuptmp"));
    }
}
