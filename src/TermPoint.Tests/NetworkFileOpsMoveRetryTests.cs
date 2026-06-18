using TermPoint.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the bounded-retry behavior of <see cref="NetworkFileOps.MoveWithRetry"/>,
/// which hardens the writeback rename (D.tmp → D) against transient destination locks
/// (antivirus / search indexer / cloud-sync clients briefly holding the file open).
///
/// <para>The transient lock is simulated by holding a <see cref="FileStream"/> open on the
/// destination with <see cref="FileShare.Read"/> — that share mode omits delete access, so the
/// overwrite's internal replace fails with <see cref="IOException"/> until the handle is released.</para>
/// </summary>
public class NetworkFileOpsMoveRetryTests
{
    /// <summary>Creates a file at <paramref name="path"/> with the given text.</summary>
    private static void WriteFile(string path, string content) => File.WriteAllText(path, content);

    [Fact]
    public void MoveWithRetry_SucceedsWhenTransientLockClearsBeforeAttemptsExhausted()
    {
        var dir = Directory.CreateTempSubdirectory("nfo_move_retry").FullName;
        try
        {
            var source = Path.Combine(dir, "source.txt");
            var dest   = Path.Combine(dir, "dest.txt");
            WriteFile(source, "new-content");
            WriteFile(dest, "old-content");

            // Hold the destination open (no delete-share) so the first Move attempt fails,
            // then release before the 3-attempt budget is exhausted so a retry succeeds.
            var lockHandle = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read);
            var releaseAfterFirstFailure = Task.Run(() =>
            {
                // Shorter than one retry delay so attempt #2 finds the destination free.
                Thread.Sleep(NetworkFileOps.MoveRetryDelayMs - 50);
                lockHandle.Dispose();
            });

            // Should not throw — a later attempt succeeds once the lock clears.
            NetworkFileOps.MoveWithRetry(source, dest);
            releaseAfterFirstFailure.Wait();

            Assert.False(File.Exists(source));               // moved away
            Assert.Equal("new-content", File.ReadAllText(dest)); // destination replaced
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MoveWithRetry_ThrowsWhenLockPersistsThroughAllAttempts()
    {
        var dir = Directory.CreateTempSubdirectory("nfo_move_persist").FullName;
        try
        {
            var source = Path.Combine(dir, "source.txt");
            var dest   = Path.Combine(dir, "dest.txt");
            WriteFile(source, "new-content");
            WriteFile(dest, "old-content");

            // Hold the destination locked for the whole retry budget: the move must give up
            // and re-throw, exactly as before the retry was added (so callers' catch still fires).
            using var lockHandle = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read);

            var ex = Record.Exception(() => NetworkFileOps.MoveWithRetry(source, dest));

            Assert.True(ex is IOException or UnauthorizedAccessException,
                $"Expected IOException/UnauthorizedAccessException, got {ex?.GetType().Name ?? "none"}");
            Assert.True(File.Exists(source));                 // source untouched on failure
            Assert.Equal("old-content", File.ReadAllText(dest)); // destination unchanged
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
