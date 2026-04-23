using System.Security.Cryptography;

namespace SchedulingAssistant.Services;

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
/// </summary>
public static class NetworkFileOps
{
    /// <summary>
    /// Maximum time to wait for a single network file operation. Operations that
    /// exceed this threshold are presumed blocked by an unreachable network share.
    /// </summary>
    public const int TimeoutMs = 5000;

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
    /// Timeout-aware SHA-256 hash of a file, opened with <see cref="FileShare.ReadWrite"/>
    /// so the hash succeeds even while another process holds the file open.
    /// </summary>
    /// <returns>
    /// <c>(true, hash)</c> when the operation completed within the deadline.
    /// <c>(false, null)</c> on timeout.
    /// </returns>
    public static Task<(bool Completed, string? Hash)> ComputeHashAsync(string path)
        => RunAsync(() => ComputeHash(path), "ComputeHash");

    /// <summary>
    /// Timeout-aware file copy using <see cref="FileShare.ReadWrite"/> on the source.
    /// </summary>
    /// <returns><c>true</c> when the copy completed within the deadline; <c>false</c> on timeout.</returns>
    public static Task<bool> CopyAsync(string source, string dest)
        => RunAsync(() => CopyWithSharing(source, dest), "CopyFile");

    /// <summary>
    /// Timeout-aware <see cref="File.Move(string, string, bool)"/> with <c>overwrite: true</c>.
    /// </summary>
    /// <returns><c>true</c> when the move completed within the deadline; <c>false</c> on timeout.</returns>
    public static Task<bool> MoveAsync(string source, string dest)
        => RunAsync(() => File.Move(source, dest, overwrite: true), "File.Move");

    /// <summary>
    /// Timeout-aware <see cref="File.Delete(string)"/>. Non-throwing on timeout — cleanup
    /// operations should not block the caller if the network is down.
    /// </summary>
    /// <returns><c>true</c> when the delete completed within the deadline; <c>false</c> on timeout.</returns>
    public static async Task<bool> DeleteAsync(string path)
    {
        try
        {
            return await RunAsync(() => File.Delete(path), "File.Delete");
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
        var winner = await Task.WhenAny(task, Task.Delay(TimeoutMs));

        if (winner == task)
            return (true, await task);

        App.Logger.LogInfo(
            $"NetworkFileOps: {label} timed out after {TimeoutMs}ms " +
            "— network may be unavailable");
        return (false, default);
    }

    /// <summary>
    /// Void variant of <see cref="RunAsync{T}"/>. Returns <c>true</c> when the
    /// operation completed within the deadline, <c>false</c> on timeout.
    /// </summary>
    /// <param name="operation">Synchronous operation to run.</param>
    /// <param name="label">Human-readable label for log messages.</param>
    public static async Task<bool> RunAsync(Action operation, string label)
    {
        var task = Task.Run(operation);
        var winner = await Task.WhenAny(task, Task.Delay(TimeoutMs));

        if (winner == task)
        {
            await task; // propagate exceptions
            return true;
        }

        App.Logger.LogInfo(
            $"NetworkFileOps: {label} timed out after {TimeoutMs}ms " +
            "— network may be unavailable");
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
    /// Copies <paramref name="source"/> to <paramref name="dest"/>, opening the
    /// source with <see cref="FileShare.ReadWrite"/> so the copy succeeds even while
    /// another process holds the file open.
    /// </summary>
    internal static void CopyWithSharing(string source, string dest)
    {
        using var src = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var dst = new FileStream(
            dest, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }
}
