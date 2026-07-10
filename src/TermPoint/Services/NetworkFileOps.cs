using System.Security.Cryptography;

namespace TermPoint.Services;

/// <summary>
/// Tri-state outcome of <see cref="NetworkFileOps.ProbeFileAsync"/>. Unlike
/// <see cref="File.Exists(string)"/> — which returns <c>false</c> for both a
/// genuinely absent file and any network failure — this distinguishes "the
/// location answered and the file is not there" from "the location could not
/// be reached at all".
/// </summary>
public enum FileProbeResult
{
    /// <summary>The file exists.</summary>
    Exists,

    /// <summary>The location responded and the file is genuinely absent.</summary>
    Missing,

    /// <summary>
    /// The location could not be reached (network error, access failure, or the
    /// probe stalled past the deadline).
    /// </summary>
    Unreachable
}

/// <summary>
/// Timeout-aware wrappers for file operations against paths that may be on a
/// network share (D, D.lock, D.tmp). Every method runs the underlying I/O on a
/// thread-pool thread and races it against a <see cref="TimeoutMs"/> deadline.
///
/// <para><b>When to use this class:</b> any file operation where the path derives
/// from <c>SourcePath</c> (the user's database on a potentially-remote drive).
/// Local paths (D' and D'' under <c>%AppData%</c>) should use <c>File.*</c>
/// directly — they are never network-bound.</para>
///
/// <para><b>On timeout:</b> the method logs via <see cref="App.Logger"/> and
/// returns a failure signal. The abandoned thread-pool thread will eventually
/// complete or throw on its own; a single leaked thread is acceptable for an
/// error path. Callers should not retry immediately — if the network is down,
/// repeated attempts just stack up abandoned threads.</para>
///
/// <para><b>On exception within the deadline:</b> the exception propagates to
/// the caller unchanged, so existing try/catch blocks continue to work.</para>
///
/// <para><b>Threading:</b> every await here uses <c>ConfigureAwait(false)</c>. Nothing
/// in this class touches UI state, and its continuations must never run on the UI
/// thread: a continuation that performs blocking network I/O (most notably a stream
/// Dispose, which flushes the SMB write-behind cache) would otherwise freeze the app
/// for the full SMB timeout when the share goes dark (field-observed 2026-07-04:
/// "Not responding" for ~40s during a mid-save outage).</para>
/// </summary>
public static class NetworkFileOps
{
    /// <summary>
    /// Maximum time to wait for a single network file operation. Operations that
    /// exceed this threshold are presumed blocked by an unreachable network share.
    ///
    /// <para><b>Semantics by operation size:</b> for metadata-sized operations
    /// (Exists, lock-file read/write, delete, rename) this is the total budget.
    /// For bulk transfers (<see cref="CopyAsync"/>, <see cref="ComputeHashAsync"/>)
    /// it is a <b>per-chunk stall deadline</b> — "no progress for this long" — so a
    /// large database on a slow-but-healthy link takes as long as it takes, while a
    /// genuinely dead share still fails within one deadline.</para>
    /// </summary>
    public const int TimeoutMs = 5000;

    /// <summary>
    /// Chunk size for bulk transfers (<see cref="CopyAsync"/>, <see cref="ComputeHashAsync"/>).
    /// Each chunk must complete within <see cref="TimeoutMs"/>, so the chunk size sets the
    /// minimum throughput a link must sustain to avoid being classified as stalled:
    /// 64 KB / 5 s ≈ <b>13 KB/s floor</b> — below any usable connection.
    ///
    /// <para><b>Why not larger?</b> This was originally 1 MB (~200 KB/s floor), which
    /// silently reintroduced the finding-#7 misclassification for any file smaller than
    /// one chunk: the whole file became a single read racing a 5-second deadline, and a
    /// degraded-but-working link (e.g. lossy WiFi at ~100 KB/s — below the floor) failed
    /// every hash/copy even though bytes were flowing continuously. Field-observed with a
    /// 404 KB database over a 74 ms / 3 %-loss link (2026-07-04): every save produced
    /// "ComputeHash (read) made no progress" despite steady progress at the wire. A
    /// smaller chunk makes progress visible to the detector at the granularity the
    /// deadline actually assumes.</para>
    /// </summary>
    internal const int ChunkSize = 1 << 16;

    /// <summary>
    /// Standard user-facing message for network timeout failures.
    /// </summary>
    public const string UnreachableMessage =
        "Cannot reach the database — check your network connection and try again.";

    // ── Named helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Timeout-aware <see cref="File.Exists(string)"/>.
    /// </summary>
    /// <returns>
    /// <c>(true, exists)</c> when the operation completed within the deadline.
    /// <c>(false, false)</c> on timeout.
    /// </returns>
    public static Task<(bool Completed, bool Exists)> ExistsAsync(string path)
        => RunAsync(() => File.Exists(path), "File.Exists");

    /// <summary>
    /// Timeout-aware <see cref="Directory.Exists(string)"/>. Useful as a
    /// lightweight reachability probe for a network share — the directory
    /// will exist as long as the share is accessible.
    /// </summary>
    /// <returns>
    /// <c>(true, exists)</c> when the operation completed within the deadline.
    /// <c>(false, false)</c> on timeout.
    /// </returns>
    public static Task<(bool Completed, bool Exists)> DirectoryExistsAsync(string path)
        => RunAsync(() => Directory.Exists(path), "Directory.Exists");

    /// <summary>
    /// Timeout-aware existence probe that distinguishes a genuinely missing file
    /// from an unreachable location. <see cref="File.Exists(string)"/> cannot make
    /// that distinction — it swallows every error and returns <c>false</c>, so a
    /// dead network share that fails <i>fast</i> (cached dead SMB session, RST)
    /// looks identical to a deleted file. This probe uses
    /// <see cref="File.GetAttributes(string)"/> instead, which surfaces the
    /// underlying Win32 error as a typed exception:
    /// <list type="bullet">
    /// <item><c>ERROR_FILE_NOT_FOUND</c> → <see cref="FileNotFoundException"/> and
    /// <c>ERROR_PATH_NOT_FOUND</c> → <see cref="DirectoryNotFoundException"/> — the
    /// location answered and the file is absent → <see cref="FileProbeResult.Missing"/>.</item>
    /// <item>Network failures (<c>ERROR_BAD_NETPATH</c>, <c>ERROR_BAD_NETNAME</c>, …)
    /// map to <see cref="IOException"/>; these and any other error →
    /// <see cref="FileProbeResult.Unreachable"/>.</item>
    /// <item>Deadline timeout (black-holed share) → <see cref="FileProbeResult.Unreachable"/>.</item>
    /// </list>
    /// Use this wherever "file not found" and "network down" must route to
    /// different UX; use <see cref="ExistsAsync"/> when the distinction doesn't matter.
    /// </summary>
    /// <param name="path">Full path of the file to probe (may be a UNC path).</param>
    /// <returns>The tri-state probe outcome; never throws.</returns>
    public static async Task<FileProbeResult> ProbeFileAsync(string path)
    {
        var (completed, result) = await RunAsync(() =>
        {
            try
            {
                File.GetAttributes(path);
                return FileProbeResult.Exists;
            }
            catch (FileNotFoundException)
            {
                return FileProbeResult.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return FileProbeResult.Missing;
            }
            catch
            {
                return FileProbeResult.Unreachable;
            }
        }, "ProbeFile").ConfigureAwait(false);

        return completed ? result : FileProbeResult.Unreachable;
    }

    /// <summary>
    /// Stall-aware SHA-256 hash of a file, opened with <see cref="FileShare.ReadWrite"/>
    /// so the hash succeeds even while another process holds the file open.
    /// The file is read in <see cref="ChunkSize"/> chunks with the <see cref="TimeoutMs"/>
    /// deadline applied per chunk, so hashing time scales with file size without a
    /// large file ever being misclassified as a network failure.
    /// </summary>
    /// <returns>
    /// <c>(true, hash)</c> when the whole file was read.
    /// <c>(false, null)</c> when the open or any single chunk stalled past the deadline.
    /// </returns>
    public static async Task<(bool Completed, string? Hash)> ComputeHashAsync(string path)
    {
        FileStream? stream = null;
        try
        {
            // The open is itself a network round-trip — give it its own deadline.
            // On timeout the abandoned open may still create the stream seconds later;
            // the continuation in the stall branch disposes it the moment that happens.
            // SequentialScan hints the OS (and the SMB redirector) to read ahead
            // aggressively — on a network path this overlaps wire transfer with hashing.
            var openTask = Task.Run(() => stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan));
            if (await Task.WhenAny(openTask, Task.Delay(TimeoutMs)).ConfigureAwait(false) != openTask)
            {
                LogStall("ComputeHash (open)");
                ObserveAbandonedTask(openTask, "ComputeHash (open)");
                // The abandoned open frequently SUCCEEDS a few seconds later, creating a
                // live FileStream that the finally below never saw (it ran while the local
                // was still null). Dispose it as soon as the open settles — a leaked handle
                // is NOT harmless here: it blocks every later open of the same file until
                // GC finalization. Field-observed 2026-07-04 (B1): a leaked handle on D.tmp
                // made every subsequent save fail "being used by another process" even
                // after the network recovered, until the app was restarted.
                _ = openTask.ContinueWith(
                    _ => DisposeInBackground(stream),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return (false, null);
            }
            await openTask.ConfigureAwait(false); // propagate open exceptions (missing file, access denied)

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[ChunkSize];
            while (true)
            {
                var (ok, read) = await ReadChunkAsync(stream!, buffer, "ComputeHash (read)").ConfigureAwait(false);
                if (!ok) return (false, null);
                if (read == 0)
                {
                    var hash = Convert.ToHexString(hasher.GetHashAndReset());
                    // Every byte is read — the hash is valid regardless of how the close
                    // goes. Still close under a deadline: SMB CLOSE is a network round
                    // trip that hangs on a black-holed share, and the caller usually
                    // renames the hashed file next (MoveWithRetry absorbs a straggling
                    // handle, but only for ~450ms).
                    var toClose = stream;
                    stream = null; // finally must not double-dispose
                    await CloseNonFatalAsync(toClose!, "ComputeHash (close)").ConfigureAwait(false);
                    return (true, hash);
                }
                hasher.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            DisposeInBackground(stream);
        }
    }

    /// <summary>
    /// Stall-aware file copy using <see cref="FileShare.ReadWrite"/> on the source, so
    /// the copy succeeds even while another process holds the file open. The transfer
    /// is chunked with the <see cref="TimeoutMs"/> deadline applied per chunk — total
    /// duration is unbounded as long as bytes keep moving, so a large database over a
    /// slow link is never misclassified as unreachable (the finding-#7 fix), while a
    /// dead share still fails within one deadline. A stalled copy leaves a partial
    /// destination file behind; callers delete it as they do today.
    /// </summary>
    /// <returns><c>true</c> when the copy completed; <c>false</c> when the open or any single chunk stalled.</returns>
    public static async Task<bool> CopyAsync(string source, string dest)
    {
        FileStream? src = null, dst = null;
        try
        {
            // See ComputeHashAsync for the open-deadline and leak-on-timeout notes.
            var openTask = Task.Run(() =>
            {
                src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize,
                    FileOptions.Asynchronous);
            });
            if (await Task.WhenAny(openTask, Task.Delay(TimeoutMs)).ConfigureAwait(false) != openTask)
            {
                LogStall("CopyFile (open)");
                ObserveAbandonedTask(openTask, "CopyFile (open)");
                // Dispose whatever the abandoned open eventually created (src, dst, or
                // both — dst is FileShare.None on D.tmp, so leaking it blocks EVERY
                // subsequent save until GC finalization; see ComputeHashAsync's stall
                // branch for the field observation). Runs on fault too: a late fault in
                // the dst constructor still leaves a live src to clean up.
                _ = openTask.ContinueWith(
                    _ => DisposeInBackground(src, dst),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return false;
            }
            await openTask.ConfigureAwait(false); // propagate open exceptions

            var buffer = new byte[ChunkSize];
            while (true)
            {
                var (ok, read) = await ReadChunkAsync(src!, buffer, "CopyFile (read)").ConfigureAwait(false);
                if (!ok) return false;
                if (read == 0)
                {
                    // Close the destination under the stall deadline BEFORE reporting
                    // success. Dispose here is not a cheap handle release: it flushes
                    // the SMB write-behind cache and frees the FileShare.None handle,
                    // both of which must complete before the caller verifies and
                    // renames D.tmp. On a black-holed share this is exactly where a
                    // late outage bites — a copy whose chunks all "completed" into the
                    // redirector's cache still has unflushed bytes on the wire.
                    var toClose = dst;
                    dst = null; // finally must not double-dispose
                    var closeTask = Task.Run(toClose!.Dispose);
                    if (await Task.WhenAny(closeTask, Task.Delay(TimeoutMs)).ConfigureAwait(false) != closeTask)
                    {
                        LogStall("CopyFile (close)");
                        ObserveAbandonedTask(closeTask, "CopyFile (close)");
                        return false;
                    }
                    await closeTask.ConfigureAwait(false); // propagate flush failures — unflushed bytes = failed copy

                    // Release the source handle before reporting success, too — callers
                    // delete the source right after a successful copy (the local
                    // .savetmp snapshot), and a still-open background dispose would
                    // make that delete fail. Non-fatal: every byte is already copied.
                    var srcToClose = src;
                    src = null; // finally must not double-dispose
                    await CloseNonFatalAsync(srcToClose!, "CopyFile (source close)").ConfigureAwait(false);
                    return true;
                }

                var writeTask = dst!.WriteAsync(buffer.AsMemory(0, read)).AsTask();
                if (await Task.WhenAny(writeTask, Task.Delay(TimeoutMs)).ConfigureAwait(false) != writeTask)
                {
                    LogStall("CopyFile (write)");
                    ObserveAbandonedTask(writeTask, "CopyFile (write)");
                    return false;
                }
                await writeTask.ConfigureAwait(false); // propagate write exceptions (disk full, …)
            }
        }
        finally
        {
            DisposeInBackground(src, dst);
        }
    }

    /// <summary>
    /// Reads one chunk from <paramref name="stream"/> with a <see cref="TimeoutMs"/>
    /// stall deadline. Exceptions from the read propagate to the caller unchanged.
    /// On stall, the abandoned read task is fault-observed so its eventual failure
    /// (e.g. "network path not found" after the SMB redirector gives up ~60s into a
    /// black-hole outage) cannot surface as an UnobservedTaskException.
    /// </summary>
    /// <returns><c>(true, bytesRead)</c> on progress (0 = EOF); <c>(false, 0)</c> on stall.</returns>
    private static async Task<(bool Ok, int Read)> ReadChunkAsync(FileStream stream, byte[] buffer, string label)
    {
        var readTask = stream.ReadAsync(buffer.AsMemory()).AsTask();
        if (await Task.WhenAny(readTask, Task.Delay(TimeoutMs)).ConfigureAwait(false) != readTask)
        {
            LogStall(label);
            ObserveAbandonedTask(readTask, label);
            return (false, 0);
        }
        return (true, await readTask.ConfigureAwait(false));
    }

    /// <summary>Logs a stall/timeout for a bulk-transfer operation.</summary>
    private static void LogStall(string label)
        => App.Logger.LogInfo(
            $"NetworkFileOps: {label} made no progress for {TimeoutMs}ms " +
            "— network may be unavailable");

    /// <summary>
    /// Closes a bulk-transfer read stream under the stall deadline, tolerating failure.
    /// Used on success paths where the data has already been fully read: neither a close
    /// fault nor a close stall can invalidate the result, but the close is still a
    /// network round trip (SMB CLOSE) that hangs on a black-holed share, so it gets a
    /// bounded wait rather than an unbounded inline Dispose.
    /// </summary>
    private static async Task CloseNonFatalAsync(FileStream stream, string label)
    {
        var closeTask = Task.Run(() =>
        {
            try { stream.Dispose(); }
            catch { /* read handle, data already consumed — nothing to lose */ }
        });
        if (await Task.WhenAny(closeTask, Task.Delay(TimeoutMs)).ConfigureAwait(false) != closeTask)
            LogStall(label); // close continues in the background; faults are swallowed above
    }

    /// <summary>
    /// Disposes bulk-transfer streams from abort paths (stall or exception) on a
    /// background thread, never inline.
    ///
    /// <para><b>Why:</b> after a stall, the abandoned chunk I/O is still pending on the
    /// stream, and on a black-holed share (packets silently dropped — a dead WiFi link,
    /// as opposed to an unplugged adapter that fails fast) <see cref="FileStream.Dispose()"/>
    /// itself blocks until the SMB redirector gives up — 30–60 s. Disposing inline in a
    /// <c>finally</c> stalled the awaiting continuation for that entire window; combined
    /// with continuations running on the UI SynchronizationContext this froze the whole
    /// app ("Not responding", field-observed 2026-07-04, stage-4 clumsy test). Abort
    /// paths never need the handle released synchronously — the startup sweep and
    /// <see cref="MoveWithRetry"/> tolerate a briefly-lingering handle.</para>
    /// </summary>
    /// <param name="streams">Streams to dispose; nulls are skipped.</param>
    private static void DisposeInBackground(params FileStream?[] streams)
    {
        foreach (var s in streams)
        {
            if (s is null) continue;
            _ = Task.Run(() =>
            {
                try { s.Dispose(); }
                catch { /* dead handle on a dead share — nothing to do */ }
            });
        }
    }

    /// <summary>
    /// Timeout-aware <see cref="File.Move(string, string, bool)"/> with <c>overwrite: true</c>,
    /// hardened against transient destination locks via <see cref="MoveWithRetry"/>.
    /// </summary>
    /// <returns><c>true</c> when the move completed within the deadline; <c>false</c> on timeout.</returns>
    public static Task<bool> MoveAsync(string source, string dest)
        => RunAsync(() => MoveWithRetry(source, dest), "File.Move");

    /// <summary>
    /// Timeout-aware <see cref="File.Delete(string)"/>. Non-throwing on timeout — cleanup
    /// operations should not block the caller if the network is down.
    /// </summary>
    /// <returns><c>true</c> when the delete completed within the deadline; <c>false</c> on timeout.</returns>
    public static async Task<bool> DeleteAsync(string path)
    {
        try
        {
            return await RunAsync(() => File.Delete(path), "File.Delete").ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Timeout-aware <see cref="File.ReadAllText(string)"/>.
    /// </summary>
    /// <returns>
    /// <c>(true, text)</c> when the operation completed within the deadline.
    /// <c>(false, null)</c> on timeout.
    /// </returns>
    public static Task<(bool Completed, string? Text)> ReadAllTextAsync(string path)
        => RunAsync(() => File.ReadAllText(path), "File.ReadAllText");

    /// <summary>
    /// Timeout-aware <see cref="File.WriteAllText(string, string)"/>.
    /// </summary>
    /// <returns><c>true</c> when the write completed within the deadline; <c>false</c> on timeout.</returns>
    public static Task<bool> WriteAllTextAsync(string path, string content)
        => RunAsync(() => File.WriteAllText(path, content), "File.WriteAllText");

    /// <summary>
    /// Timeout-aware <see cref="BackupService.CheckIntegrity(string)"/>.
    /// </summary>
    /// <returns>
    /// <c>(true, passed)</c> when the operation completed within the deadline.
    /// <c>(false, false)</c> on timeout.
    /// </returns>
    public static Task<(bool Completed, bool Passed)> CheckIntegrityAsync(string path)
        => RunAsync(() => BackupService.CheckIntegrity(path), "CheckIntegrity");

    // ── Generic fallbacks ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs a synchronous operation on a thread-pool thread and races it against
    /// the <see cref="TimeoutMs"/> deadline. Use this for operations that don't
    /// have a named helper above (e.g. <c>_lockService.TryAcquire</c>).
    /// </summary>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <param name="operation">Synchronous operation to run.</param>
    /// <param name="label">Human-readable label for log messages.</param>
    /// <returns>
    /// <c>(true, result)</c> when the operation completed within the deadline.
    /// <c>(false, default)</c> on timeout.
    /// </returns>
    public static async Task<(bool Completed, T? Result)> RunAsync<T>(
        Func<T> operation, string label)
    {
        var task = Task.Run(operation);
        var winner = await Task.WhenAny(task, Task.Delay(TimeoutMs)).ConfigureAwait(false);

        if (winner == task)
            return (true, await task.ConfigureAwait(false));

        App.Logger.LogInfo(
            $"NetworkFileOps: {label} timed out after {TimeoutMs}ms " +
            "— network may be unavailable");
        ObserveAbandonedTask(task, label);
        return (false, default);
    }

    /// <summary>
    /// Attaches a fault-observing continuation to a task we are about to abandon after a
    /// timeout. The underlying thread-pool thread keeps running and may eventually throw
    /// (e.g. the SMB redirector finally surfaces <c>ERROR_SHARING_VIOLATION</c> or a
    /// connection error after we have already returned). Without an observer, that fault
    /// resurfaces as an <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>
    /// on the finalizer thread — noise at best, a process-kill on stricter configurations.
    /// Accessing <see cref="Task.Exception"/> in the continuation marks it observed.
    /// </summary>
    private static void ObserveAbandonedTask(Task task, string label)
        => _ = task.ContinueWith(
            t => App.Logger.LogInfo(
                $"NetworkFileOps: abandoned {label} task faulted after timeout: " +
                $"{t.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// <summary>
    /// Void variant of <see cref="RunAsync{T}"/>. Returns <c>true</c> when the
    /// operation completed within the deadline, <c>false</c> on timeout.
    /// </summary>
    /// <param name="operation">Synchronous operation to run.</param>
    /// <param name="label">Human-readable label for log messages.</param>
    public static async Task<bool> RunAsync(Action operation, string label)
    {
        var task = Task.Run(operation);
        var winner = await Task.WhenAny(task, Task.Delay(TimeoutMs)).ConfigureAwait(false);

        if (winner == task)
        {
            await task.ConfigureAwait(false); // propagate exceptions
            return true;
        }

        App.Logger.LogInfo(
            $"NetworkFileOps: {label} timed out after {TimeoutMs}ms " +
            "— network may be unavailable");
        ObserveAbandonedTask(task, label);
        return false;
    }

    // ── Internal implementations ──────────────────────────────────────────────

    /// <summary>
    /// SHA-256 hash of a file, opened with <see cref="FileShare.ReadWrite"/> so
    /// the read succeeds even while <c>DatabaseContext</c> holds the file open.
    /// </summary>
    internal static string ComputeHash(string filePath)
    {
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Number of attempts <see cref="MoveWithRetry"/> makes before giving up.
    /// </summary>
    internal const int MoveRetryAttempts = 3;

    /// <summary>
    /// Delay between <see cref="MoveWithRetry"/> attempts, in milliseconds.
    /// </summary>
    internal const int MoveRetryDelayMs = 150;

    /// <summary>
    /// <see cref="File.Move(string, string, bool)"/> (overwrite) with a bounded retry.
    ///
    /// <para>The atomic replace of an existing destination can fail with a transient
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when an
    /// external process is momentarily holding the destination (or the just-written
    /// source) open — most commonly antivirus real-time scanning, the Windows Search
    /// indexer, or a cloud-sync client (OneDrive/Dropbox). These locks clear in well
    /// under a second, so we retry up to <see cref="MoveRetryAttempts"/> times with a
    /// <see cref="MoveRetryDelayMs"/> pause between attempts.</para>
    ///
    /// <para>Runs on a thread-pool thread (inside <see cref="RunAsync(Action, string)"/>),
    /// so a blocking <see cref="Thread.Sleep(int)"/> between attempts is acceptable. The
    /// whole sequence is still bounded by the caller's <see cref="TimeoutMs"/> deadline.
    /// On the final attempt the exception is re-thrown unchanged so genuine (persistent)
    /// failures propagate to the caller exactly as before.</para>
    /// </summary>
    /// <param name="source">Path to move from.</param>
    /// <param name="dest">Path to move to; overwritten if it exists.</param>
    /// <exception cref="IOException">Persistent I/O failure after all attempts.</exception>
    /// <exception cref="UnauthorizedAccessException">Persistent access failure after all attempts.</exception>
    internal static void MoveWithRetry(string source, string dest)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                (ex is IOException || ex is UnauthorizedAccessException)
                && attempt < MoveRetryAttempts)
            {
                App.Logger.LogInfo(
                    $"NetworkFileOps: File.Move attempt {attempt}/{MoveRetryAttempts} " +
                    $"failed ({ex.GetType().Name}) — likely a transient lock; " +
                    $"retrying in {MoveRetryDelayMs}ms");
                Thread.Sleep(MoveRetryDelayMs);
            }
        }
    }

}
