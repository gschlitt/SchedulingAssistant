using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the reader-side lock polling in <see cref="WriteLockService.PollLockFile"/>.
///
/// <para>Regression focus: a transient read failure (sharing violation from the writer's
/// heartbeat rename, or a momentary not-found) must SKIP the poll cycle — it must never be
/// classified as "infinitely stale". Before the fix, an unreadable lock file set
/// <c>CurrentHolder = null</c>, the age computation treated null as <c>double.MaxValue</c>,
/// and a single 5&#160;ms collision permanently flipped <see cref="WriteLockService.WriteLockBecameAvailable"/>
/// (the poll timer is disposed on the first available=true) — offering this reader a takeover
/// of a LIVE writer's lock.</para>
///
/// <para>Each test uses an isolated temp directory. <see cref="WriteLockService.EnterReaderMode"/>
/// puts the service into reader mode without contending for the lock, and
/// <see cref="WriteLockService.PollLockFile"/> (internal, exposed for tests) is invoked
/// directly, bypassing the poll timer and UI-thread dispatch.</para>
/// </summary>
public sealed class WriteLockServicePollTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for the fake database and lock file.</summary>
    public WriteLockServicePollTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wlsp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Deletes the temporary directory and all files created during the test.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Path of the fake database file inside the temp directory.</summary>
    private string DbPath => Path.Combine(_tempDir, "test.db");

    /// <summary>Path of the lock file that sits alongside <see cref="DbPath"/>.</summary>
    private string LockPath => Path.ChangeExtension(DbPath, ".lock");

    /// <summary>
    /// Writes a lock file owned by a foreign session with the given heartbeat timestamp.
    /// A recent heartbeat simulates a live remote writer; an old one simulates a crashed writer.
    /// </summary>
    private void WriteForeignLock(DateTime heartbeatUtc)
    {
        var data = new LockFileData
        {
            Username    = "other_user",
            Machine     = "OTHER-PC",
            SessionGuid = Guid.NewGuid().ToString(),
            Acquired    = heartbeatUtc,
            Heartbeat   = heartbeatUtc,
        };
        File.WriteAllText(LockPath, JsonSerializer.Serialize(data));
    }

    /// <summary>Creates a reader-mode <see cref="WriteLockService"/> pointed at <see cref="DbPath"/>.</summary>
    private WriteLockService CreateReader()
    {
        File.WriteAllText(DbPath, "dummy-database-content");
        var svc = new WriteLockService();
        svc.EnterReaderMode(DbPath);
        Assert.False(svc.IsWriter); // precondition: reader mode
        return svc;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression: when the lock file exists but the read is refused with a sharing
    /// violation (here forced deterministically by holding it open with
    /// <see cref="FileShare.None"/>, mimicking the writer's heartbeat rename window),
    /// the poll must skip the cycle — NOT offer write access. Before the fix this
    /// flipped a permanent "switch to edit mode" offer against a live writer.
    /// </summary>
    [Fact]
    public async Task PollLockFile_LockReadContended_SkipsCycle_NoFalseAvailability()
    {
        WriteForeignLock(DateTime.UtcNow); // fresh heartbeat — a LIVE remote writer
        using var svc = CreateReader();

        using (var holder = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await svc.PollLockFile();
        }

        Assert.False(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// Control: a readable lock with a FRESH heartbeat is respected — no availability offer.
    /// Confirms the contended-read test above isn't passing vacuously.
    /// </summary>
    [Fact]
    public async Task PollLockFile_FreshLock_NoAvailabilityOffer()
    {
        WriteForeignLock(DateTime.UtcNow);
        using var svc = CreateReader();

        await svc.PollLockFile();

        Assert.False(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// Control: a readable lock whose heartbeat is older than the stale threshold DOES
    /// offer write access. Confirms the skip-on-contention fix did not suppress the
    /// legitimate stale-lock detection path.
    /// </summary>
    [Fact]
    public async Task PollLockFile_StaleLock_OffersWriteAccess()
    {
        WriteForeignLock(DateTime.UtcNow.AddSeconds(-(WriteLockService.StaleLockThresholdSeconds + 30)));
        using var svc = CreateReader();

        await svc.PollLockFile();

        Assert.True(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// Control: a readable but CORRUPT lock file is deliberately classified as stale
    /// (an unusable lock should be offered for takeover, not respected forever) —
    /// the documented pre-existing semantics, preserved by the fix.
    /// </summary>
    [Fact]
    public async Task PollLockFile_CorruptLock_TreatedAsStale_OffersWriteAccess()
    {
        File.WriteAllText(LockPath, "{ this is not valid json !!");
        using var svc = CreateReader();

        await svc.PollLockFile();

        Assert.True(svc.WriteLockBecameAvailable);
    }
}
