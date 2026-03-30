using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="WriteLockService"/>.
///
/// <para>Each test fixture creates an isolated temporary directory whose path is
/// used as a fake database directory. No real SQLite database is opened — only
/// the <c>.lock</c> file mechanics are exercised.</para>
///
/// <para>The Avalonia UI thread is never involved: timer callbacks are bypassed by
/// calling <see cref="WriteLockService.PollLockFile"/> directly (exposed as
/// <c>internal</c> for this purpose).</para>
///
/// <para>Tests are organised into five groups:
/// <list type="bullet">
///   <item><description>Acquisition — first-open and contested-open behaviour</description></item>
///   <item><description>Stale reclaim — a dead writer's lock is reclaimed on open</description></item>
///   <item><description>Release / Dispose — clean shutdown and idempotency</description></item>
///   <item><description>Database switching — re-acquisition on a different file</description></item>
///   <item><description>Polling — read-only instance detects lock gone or stale</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class WriteLockServiceTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>
    /// Creates a unique temporary directory for this test instance.
    /// All <c>.db</c> and <c>.lock</c> paths used in the test live here.
    /// </summary>
    public WriteLockServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wls_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Deletes the temporary directory and all files created during the test.
    /// Services are disposed before cleanup to ensure lock files are released.
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a fake <c>.db</c> path inside the temp directory.
    /// The file does not need to exist; only the directory is required.
    /// </summary>
    private string DbPath(string name = "test") =>
        Path.Combine(_tempDir, $"{name}.db");

    /// <summary>
    /// Returns the expected <c>.lock</c> path for a given <c>.db</c> path.
    /// </summary>
    private static string LockPath(string dbPath) =>
        Path.ChangeExtension(dbPath, ".lock");

    /// <summary>
    /// Writes a lock file with the given heartbeat age (in seconds) into
    /// the temp directory. Used to simulate a live or stale remote writer.
    /// </summary>
    /// <param name="lockPath">Full path of the lock file to create.</param>
    /// <param name="heartbeatAgeSeconds">
    /// How many seconds ago the heartbeat was last written.
    /// Use a value greater than <see cref="WriteLockService.StaleLockThresholdSeconds"/>
    /// to simulate a crashed writer.
    /// </param>
    private static void WriteExternalLockFile(string lockPath, int heartbeatAgeSeconds = 0)
    {
        var now = DateTime.UtcNow;
        var data = new LockFileData
        {
            Username  = "external_user",
            Machine   = "REMOTE-PC",
            Acquired  = now.AddSeconds(-heartbeatAgeSeconds),
            Heartbeat = now.AddSeconds(-heartbeatAgeSeconds),
        };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(data));
    }

    /// <summary>
    /// Reads and deserializes the lock file at the given path.
    /// Throws if the file does not exist or is malformed.
    /// </summary>
    private static LockFileData ReadLockFile(string lockPath) =>
        JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(lockPath))
        ?? throw new InvalidOperationException("Lock file was empty or null.");

    // ═════════════════════════════════════════════════════════════════════════
    // Group 1 — Acquisition
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When no lock file exists, the first caller becomes the writer.
    /// </summary>
    [Fact]
    public void TryAcquire_NoLockFile_BecomesWriter()
    {
        using var svc = new WriteLockService();
        svc.TryAcquire(DbPath());
        Assert.True(svc.IsWriter);
    }

    /// <summary>
    /// A successful acquisition creates the lock file on disk.
    /// </summary>
    [Fact]
    public void TryAcquire_NoLockFile_CreatesLockFile()
    {
        using var svc = new WriteLockService();
        var db = DbPath();
        svc.TryAcquire(db);
        Assert.True(File.Exists(LockPath(db)));
    }

    /// <summary>
    /// The lock file written on acquisition contains valid JSON with the
    /// current user's credentials and timestamps within the last few seconds.
    /// </summary>
    [Fact]
    public void TryAcquire_NoLockFile_LockFileContainsValidJson()
    {
        using var svc = new WriteLockService();
        var db = DbPath();
        var before = DateTime.UtcNow;

        svc.TryAcquire(db);

        var data = ReadLockFile(LockPath(db));
        Assert.Equal(Environment.UserName,    data.Username);
        Assert.Equal(Environment.MachineName, data.Machine);
        Assert.True(data.Acquired  >= before, "Acquired should be after test start.");
        Assert.True(data.Heartbeat >= before, "Heartbeat should be after test start.");
        Assert.True(data.Acquired  <= DateTime.UtcNow, "Acquired should not be in the future.");
    }

    /// <summary>
    /// When a lock file with a fresh heartbeat already exists, the second
    /// caller becomes a reader (IsWriter = false).
    /// </summary>
    [Fact]
    public void TryAcquire_FreshLockExists_BecomesReader()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        Assert.False(reader.IsWriter);
    }

    /// <summary>
    /// A reader's <see cref="WriteLockService.CurrentHolder"/> is populated with the
    /// data written by the first instance.
    /// </summary>
    [Fact]
    public void TryAcquire_FreshLockExists_PopulatesCurrentHolder()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        Assert.NotNull(reader.CurrentHolder);
        Assert.Equal(Environment.UserName,    reader.CurrentHolder!.Username);
        Assert.Equal(Environment.MachineName, reader.CurrentHolder.Machine);
    }

    /// <summary>
    /// A reader starts with <see cref="WriteLockService.WriteLockBecameAvailable"/>
    /// set to false — it has not yet detected any change.
    /// </summary>
    [Fact]
    public void TryAcquire_FreshLockExists_WriteLockBecameAvailableIsFalse()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        Assert.False(reader.WriteLockBecameAvailable);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 2 — Stale lock detection and ForceAcquire
    //
    // The service no longer auto-reclaims a stale lock. Instead it sets
    // IsStaleLock = true so the caller can prompt the user for confirmation
    // before calling ForceAcquire().
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the existing lock file's heartbeat is older than
    /// <see cref="WriteLockService.StaleLockThresholdSeconds"/>, TryAcquire
    /// sets <see cref="WriteLockService.IsStaleLock"/> to true instead of
    /// auto-reclaiming — caller must confirm and then call ForceAcquire.
    /// </summary>
    [Fact]
    public void TryAcquire_StaleLockExists_SetsIsStaleLock()
    {
        var db    = DbPath();
        var lock_ = LockPath(db);
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds + 1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db);

        Assert.True(svc.IsStaleLock);
    }

    /// <summary>
    /// When <see cref="WriteLockService.IsStaleLock"/> is true, the instance is
    /// NOT the writer — it has not taken the lock yet.
    /// </summary>
    [Fact]
    public void TryAcquire_StaleLockExists_IsWriterIsFalse()
    {
        var db    = DbPath();
        var lock_ = LockPath(db);
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds + 1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db);

        Assert.False(svc.IsWriter);
    }

    /// <summary>
    /// When <see cref="WriteLockService.IsStaleLock"/> is true, the stale
    /// holder's data is still available in <see cref="WriteLockService.CurrentHolder"/>.
    /// The caller needs this to display the holder's name in the prompt.
    /// </summary>
    [Fact]
    public void TryAcquire_StaleLockExists_CurrentHolderPopulatedWithOldData()
    {
        var db    = DbPath();
        var lock_ = LockPath(db);
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds + 1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db);

        Assert.NotNull(svc.CurrentHolder);
        Assert.Equal("external_user", svc.CurrentHolder!.Username);
        Assert.Equal("REMOTE-PC",     svc.CurrentHolder.Machine);
    }

    /// <summary>
    /// After the user confirms via <see cref="WriteLockService.ForceAcquire"/>,
    /// the instance becomes the writer.
    /// </summary>
    [Fact]
    public void ForceAcquire_AfterStaleLock_BecomesWriter()
    {
        var db    = DbPath();
        var lock_ = LockPath(db);
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds + 1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db);
        Assert.True(svc.IsStaleLock); // precondition

        svc.ForceAcquire();

        Assert.True(svc.IsWriter);
        Assert.False(svc.IsStaleLock);
    }

    /// <summary>
    /// After <see cref="WriteLockService.ForceAcquire"/>, the lock file is
    /// overwritten with this instance's credentials.
    /// </summary>
    [Fact]
    public void ForceAcquire_AfterStaleLock_OverwritesLockFileWithNewData()
    {
        var db    = DbPath();
        var lock_ = LockPath(db);
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds + 1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db);
        svc.ForceAcquire();

        var data = ReadLockFile(lock_);
        Assert.Equal(Environment.UserName,    data.Username);
        Assert.Equal(Environment.MachineName, data.Machine);
    }

    /// <summary>
    /// A lock file whose heartbeat is one second below the stale threshold is
    /// treated as live; the second opener becomes a reader, not a writer.
    /// Testing at exactly the threshold is inherently racy (clock advances between
    /// write and read), so we stay one second inside the "live" zone.
    /// </summary>
    [Fact]
    public void TryAcquire_HeartbeatJustBelowThreshold_TreatedAsLive()
    {
        var db    = DbPath();
        var lock_ = LockPath(db);
        // One second below the threshold — unambiguously live regardless of timing jitter.
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds - 1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db);

        Assert.False(svc.IsWriter);
        Assert.False(svc.IsStaleLock);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 3 — Release and Dispose
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <see cref="WriteLockService.Release"/> deletes the lock file.
    /// </summary>
    [Fact]
    public void Release_AsWriter_DeletesLockFile()
    {
        var db = DbPath();
        using var svc = new WriteLockService();
        svc.TryAcquire(db);
        Assert.True(File.Exists(LockPath(db)));

        svc.Release();

        Assert.False(File.Exists(LockPath(db)));
    }

    /// <summary>
    /// After <see cref="WriteLockService.Release"/>, <c>IsWriter</c> is false.
    /// </summary>
    [Fact]
    public void Release_AsWriter_SetsIsWriterFalse()
    {
        using var svc = new WriteLockService();
        svc.TryAcquire(DbPath());
        svc.Release();
        Assert.False(svc.IsWriter);
    }

    /// <summary>
    /// Calling <see cref="WriteLockService.Release"/> from read-only mode does
    /// not throw and leaves the other instance's lock file intact.
    /// </summary>
    [Fact]
    public void Release_AsReader_DoesNotDeleteWritersLockFile()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        reader.Release(); // Should be a no-op for the lock file.

        Assert.True(File.Exists(LockPath(db)));
    }

    /// <summary>
    /// Calling <see cref="WriteLockService.Release"/> twice on a writer does not
    /// throw (idempotent).
    /// </summary>
    [Fact]
    public void Release_CalledTwice_NoException()
    {
        using var svc = new WriteLockService();
        svc.TryAcquire(DbPath());
        svc.Release();
        var ex = Record.Exception(() => svc.Release());
        Assert.Null(ex);
    }

    /// <summary>
    /// <see cref="WriteLockService.Dispose"/> behaves identically to
    /// <see cref="WriteLockService.Release"/> — the lock file is deleted.
    /// </summary>
    [Fact]
    public void Dispose_AsWriter_DeletesLockFile()
    {
        var db = DbPath();
        var svc = new WriteLockService();
        svc.TryAcquire(db);
        Assert.True(File.Exists(LockPath(db)));

        svc.Dispose();

        Assert.False(File.Exists(LockPath(db)));
    }

    /// <summary>
    /// Calling <see cref="WriteLockService.Dispose"/> more than once does not throw.
    /// </summary>
    [Fact]
    public void Dispose_CalledTwice_NoException()
    {
        var svc = new WriteLockService();
        svc.TryAcquire(DbPath());
        svc.Dispose();
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 4 — Database switching
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calling <see cref="WriteLockService.TryAcquire"/> a second time (simulating
    /// the user switching databases) releases the old lock and acquires the new one.
    /// </summary>
    [Fact]
    public void TryAcquire_CalledTwice_ReleasesFirstLockAndAcquiresSecond()
    {
        using var svc = new WriteLockService();
        var db1 = DbPath("first");
        var db2 = DbPath("second");

        svc.TryAcquire(db1);
        Assert.True(File.Exists(LockPath(db1)));

        svc.TryAcquire(db2);

        Assert.False(File.Exists(LockPath(db1)), "First lock should be released.");
        Assert.True(File.Exists(LockPath(db2)),  "Second lock should be created.");
        Assert.True(svc.IsWriter);
    }

    /// <summary>
    /// After switching databases, <see cref="WriteLockService.WriteLockBecameAvailable"/>
    /// is reset to false regardless of any prior state.
    /// </summary>
    [Fact]
    public void TryAcquire_AfterSwitch_ResetsWriteLockBecameAvailable()
    {
        var db1 = DbPath("first");
        var db2 = DbPath("second");

        using var other = new WriteLockService();
        other.TryAcquire(db1);

        using var svc = new WriteLockService();
        svc.TryAcquire(db1); // becomes reader
        other.Release();      // simulate other closing
        svc.PollLockFile();   // sets WriteLockBecameAvailable = true

        Assert.True(svc.WriteLockBecameAvailable);

        // Switching to a new DB should clear the flag.
        svc.TryAcquire(db2);
        Assert.False(svc.WriteLockBecameAvailable);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 5 — Polling (PollLockFile called directly)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the lock file disappears (writer cleanly released), the next poll
    /// sets <see cref="WriteLockService.WriteLockBecameAvailable"/> to true.
    /// </summary>
    [Fact]
    public void PollLockFile_LockFileGone_SetsWriteLockBecameAvailable()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        writer.Release(); // Deletes the lock file.
        reader.PollLockFile();

        Assert.True(reader.WriteLockBecameAvailable);
    }

    /// <summary>
    /// When the lock file disappears, the poll raises
    /// <see cref="WriteLockService.LockStateChanged"/>.
    /// </summary>
    [Fact]
    public void PollLockFile_LockFileGone_FiresLockStateChangedEvent()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        var eventFired = false;
        reader.LockStateChanged += () => eventFired = true;

        writer.Release();
        reader.PollLockFile();

        Assert.True(eventFired);
    }

    /// <summary>
    /// When the lock file's heartbeat is stale (writer crashed), the poll
    /// sets <see cref="WriteLockService.WriteLockBecameAvailable"/> to true.
    /// </summary>
    [Fact]
    public void PollLockFile_StaleLock_SetsWriteLockBecameAvailable()
    {
        var db   = DbPath();
        var lock_ = LockPath(db);

        // Simulate a live writer so the reader enters read-only mode.
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: 0);
        using var reader = new WriteLockService();
        reader.TryAcquire(db);
        Assert.False(reader.IsWriter);

        // Overwrite the lock file with a stale heartbeat (simulate crash).
        WriteExternalLockFile(lock_, heartbeatAgeSeconds: WriteLockService.StaleLockThresholdSeconds + 1);
        reader.PollLockFile();

        Assert.True(reader.WriteLockBecameAvailable);
    }

    /// <summary>
    /// When the lock file has a fresh heartbeat, the poll does not signal
    /// availability — the writer is still alive.
    /// </summary>
    [Fact]
    public void PollLockFile_FreshLock_DoesNotSetAvailable()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);

        reader.PollLockFile(); // Writer is still alive.

        Assert.False(reader.WriteLockBecameAvailable);
    }

    /// <summary>
    /// Once <see cref="WriteLockService.WriteLockBecameAvailable"/> is true,
    /// subsequent polls are no-ops — the event does not fire a second time.
    /// </summary>
    [Fact]
    public void PollLockFile_AlreadyAvailable_EventNotFiredAgain()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);
        writer.Release();

        reader.PollLockFile(); // First poll — sets available, fires event.

        var eventCount = 0;
        reader.LockStateChanged += () => eventCount++;
        reader.PollLockFile(); // Second poll — should be a no-op.

        Assert.Equal(0, eventCount);
    }

    /// <summary>
    /// A writer instance calling <see cref="WriteLockService.PollLockFile"/>
    /// directly is a no-op — it is not a reader and should not change state.
    /// </summary>
    [Fact]
    public void PollLockFile_CalledOnWriter_DoesNothing()
    {
        using var svc = new WriteLockService();
        svc.TryAcquire(DbPath());
        Assert.True(svc.IsWriter);

        var eventFired = false;
        svc.LockStateChanged += () => eventFired = true;
        svc.PollLockFile();

        Assert.True(svc.IsWriter);
        Assert.False(svc.WriteLockBecameAvailable);
        Assert.False(eventFired);
    }

    /// <summary>
    /// After the "Switch to edit mode" prompt fires, calling
    /// <see cref="WriteLockService.TryAcquire"/> on the same path lets the
    /// formerly read-only instance become the writer.
    /// </summary>
    [Fact]
    public void PollLockFile_ThenTryAcquire_BecomesWriter()
    {
        var db = DbPath();
        using var writer = new WriteLockService();
        writer.TryAcquire(db);

        using var reader = new WriteLockService();
        reader.TryAcquire(db);
        Assert.False(reader.IsWriter);

        writer.Release();
        reader.PollLockFile();
        Assert.True(reader.WriteLockBecameAvailable);

        // User clicks "Switch to edit mode" — re-acquire.
        reader.TryAcquire(db);
        Assert.True(reader.IsWriter);
        Assert.False(reader.WriteLockBecameAvailable);
    }
}
