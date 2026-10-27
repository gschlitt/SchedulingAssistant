using System.Text.Json;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 7, step 7c: <see cref="SessionRunMarker"/> notices sessions that ended without exiting normally. Each test
/// works in its own folder, with a liveness check it controls (which made-up process ids are "running"), so no real
/// process is consulted except in the one test that says so.
/// </summary>
public sealed class SessionRunMarkerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"srm_{Guid.NewGuid():N}");
    private readonly List<string> _log = new();

    /// <summary>Deletes the test folder (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A marker on the test folder whose liveness check says only <paramref name="runningIds"/> are running.</summary>
    private SessionRunMarker Marker(params int[] runningIds)
        => new(_folder, (id, _) => runningIds.Contains(id), _log.Add);

    /// <summary>Writes a running file as another process would have.</summary>
    private void LeaveRunFile(int processId, string version, DateTime started)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, $"{processId}.run"),
            JsonSerializer.Serialize(new SessionRunMarker.RunInfo(processId, started, version)));
    }

    private static SessionRunMarker.RunInfo Self => new(9999, DateTime.UtcNow, "1.2.3.1");

    /// <summary>A first start finds nothing and writes this process's own file; a normal exit deletes it.</summary>
    [Fact]
    public void AFirstStart_FindsNothing_AndANormalExitLeavesNothing()
    {
        var marker = Marker();

        Assert.Empty(marker.Start(Self));
        Assert.True(File.Exists(Path.Combine(_folder, "9999.run")));

        marker.End();
        marker.End();   // safe twice
        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>
    /// Files left by processes that are gone are the sessions that ended unexpectedly: returned oldest first and deleted.
    /// A file whose process is still running (another TermPoint) is left alone and not returned.
    /// </summary>
    [Fact]
    public void FilesOfGoneProcesses_AreReturnedAndDeleted_ARunningOnesFileIsKept()
    {
        LeaveRunFile(101, "1.2.2", new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc));
        LeaveRunFile(102, "1.2.3.1", new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc));
        LeaveRunFile(200, "1.2.3.1", new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));

        var ended = Marker(200).Start(Self);

        Assert.Equal(new[] { 101, 102 }, ended.Select(e => e.ProcessId));
        Assert.Equal("1.2.3.1", ended[^1].AppVersion);
        Assert.False(File.Exists(Path.Combine(_folder, "101.run")));
        Assert.False(File.Exists(Path.Combine(_folder, "102.run")));
        Assert.True(File.Exists(Path.Combine(_folder, "200.run")));
    }

    /// <summary>An unreadable file is removed without being counted (it can't be told apart from one being written).</summary>
    [Fact]
    public void AnUnreadableFile_IsRemoved_NotCounted()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "300.run"), "{ not json");

        Assert.Empty(Marker().Start(Self));
        Assert.False(File.Exists(Path.Combine(_folder, "300.run")));
        Assert.Contains(_log, line => line.Contains("unreadable"));
    }

    /// <summary>
    /// The real liveness check, on this test process's own record: it is running, so a file left with it is not
    /// counted as ended.
    /// </summary>
    [Fact]
    public void TheRealCheck_SeesThisProcessAsRunning()
    {
        var me = SessionRunMarker.ForThisProcess("test");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, $"{me.ProcessId}.run"), JsonSerializer.Serialize(me));

        var ended = new SessionRunMarker(_folder).Start(new SessionRunMarker.RunInfo(1, DateTime.UtcNow, "test"));

        Assert.Empty(ended);
        Assert.Equal(Environment.ProcessId, me.ProcessId);
    }
}
