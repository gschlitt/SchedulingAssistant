using Avalonia.Platform.Storage;

namespace TermPoint.Services;

/// <summary>
/// How one attempt to write a file the user picked ended (<see cref="ExportFileWriter"/>).
/// </summary>
public enum ExportWriteOutcome
{
    /// <summary>The whole file was written within the deadline.</summary>
    Written,

    /// <summary>The folder did not answer within <see cref="NetworkFileOps.TimeoutMs"/>. The write may still land late.</summary>
    TimedOut,

    /// <summary>The write was refused or failed within the deadline (access denied, file in use, disk full, folder gone).</summary>
    Failed
}

/// <summary>
/// The outcome of <see cref="ExportFileWriter.WriteAsync(IStorageFile, byte[])"/>: whether the file was written, and,
/// when it was not, the exact plain-language message to show the user.
/// </summary>
/// <param name="Outcome">How the write ended.</param>
/// <param name="FileName">The file's name only (never a path), for the message.</param>
/// <param name="Reason">For <see cref="ExportWriteOutcome.Failed"/>: a plain sentence fragment saying why (no path, no exception text); null otherwise.</param>
public readonly record struct ExportWriteResult(ExportWriteOutcome Outcome, string FileName, string? Reason = null)
{
    /// <summary>True when the file was written.</summary>
    public bool Succeeded => Outcome == ExportWriteOutcome.Written;

    /// <summary>
    /// What to tell the user: null when the file was written, otherwise the one message every export shows
    /// (<see cref="ExportFileWriter.MessageFor"/>).
    /// </summary>
    public string? Message => ExportFileWriter.MessageFor(this);
}

/// <summary>
/// One place for "write a file the user picked" (1.2.4 item 4.10, group 3). Every export (shared schedule, course
/// history, workload report, schedule PNG) used to write straight to the picked path on the UI thread; when the folder
/// is on a network share that has stopped answering, that froze the window for the SMB timeout and then showed
/// exception text. This writer runs the write under the <see cref="NetworkFileOps"/> deadline and turns every ending
/// into a result whose <see cref="ExportWriteResult.Message"/> says what happened, that nothing else was affected, and
/// what to do next. The exception, if any, goes to the local log, never to the screen.
///
/// <para>A write that times out keeps running on its abandoned thread (see <see cref="NetworkFileOps"/>): the file may
/// appear later. The message therefore never claims the file is absent.</para>
/// </summary>
public static class ExportFileWriter
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="file"/>. When the provider gives a filesystem path
    /// (<c>file.TryGetLocalPath()</c>) the write goes through
    /// <see cref="NetworkFileOps.WriteAllBytesAsync"/>; otherwise (a non-filesystem provider) it falls back to
    /// <see cref="IStorageFile.OpenWriteAsync"/>, run under the same deadline. Never throws.
    /// </summary>
    /// <param name="file">The file the user picked in the save dialog.</param>
    /// <param name="bytes">The complete, already-encoded file contents (the caller chooses the encoding, so a CSV can keep its BOM).</param>
    /// <returns>The outcome; use <see cref="ExportWriteResult.Message"/> for the text to show on failure.</returns>
    public static async Task<ExportWriteResult> WriteAsync(IStorageFile file, byte[] bytes)
    {
        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
            return await WriteAsync(localPath, bytes).ConfigureAwait(true);

        // A provider with no filesystem path: no UNC path to stall on in the usual case, but still bounded.
        var name = file.Name;
        try
        {
            var completed = await RunBoundedAsync(() =>
            {
                using var stream = file.OpenWriteAsync().GetAwaiter().GetResult();
                stream.Write(bytes, 0, bytes.Length);
            }).ConfigureAwait(true);

            return completed
                ? new ExportWriteResult(ExportWriteOutcome.Written, name)
                : new ExportWriteResult(ExportWriteOutcome.TimedOut, name);
        }
        catch (Exception ex)
        {
            return FailedResult(name, ex);
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to the file at <paramref name="path"/> under the network deadline. This is the
    /// overload <see cref="WriteAsync(IStorageFile, byte[])"/> uses when it has a path, and the one tests call (it needs
    /// no storage provider). Never throws.
    /// </summary>
    /// <param name="path">Full path of the file to create or overwrite.</param>
    /// <param name="bytes">The complete, already-encoded file contents.</param>
    /// <returns>The outcome; the file name in it is the last segment of <paramref name="path"/>.</returns>
    public static async Task<ExportWriteResult> WriteAsync(string path, byte[] bytes)
    {
        var name = Path.GetFileName(path);
        try
        {
            return await WriteBytesBoundedAsync(path, bytes).ConfigureAwait(true)
                ? new ExportWriteResult(ExportWriteOutcome.Written, name)
                : new ExportWriteResult(ExportWriteOutcome.TimedOut, name);
        }
        catch (Exception ex)
        {
            return FailedResult(name, ex);
        }
    }

    /// <summary>
    /// <see cref="NetworkFileOps.WriteAllBytesAsync"/> on desktop. The browser (WASM demo) build does not compile
    /// <see cref="NetworkFileOps"/> (it has no file system and no network share), and no export can run there
    /// (<see cref="PlatformCapabilities.SupportsFileDialogs"/> is false), so there this is a plain synchronous write
    /// that exists only to keep the shared code compiling.
    /// </summary>
    /// <param name="path">Full path of the file to create or overwrite.</param>
    /// <param name="bytes">The complete file contents.</param>
    /// <returns><c>true</c> when written; <c>false</c> on a time-out (desktop only).</returns>
    private static Task<bool> WriteBytesBoundedAsync(string path, byte[] bytes)
    {
#if BROWSER
        File.WriteAllBytes(path, bytes);
        return Task.FromResult(true);
#else
        return NetworkFileOps.WriteAllBytesAsync(path, bytes);
#endif
    }

    /// <summary>
    /// Runs <paramref name="operation"/> under the <see cref="NetworkFileOps"/> deadline on desktop (see
    /// <see cref="WriteBytesBoundedAsync"/> for why the browser build differs).
    /// </summary>
    /// <param name="operation">The synchronous write to run on a thread-pool thread.</param>
    /// <returns><c>true</c> when it finished in time; <c>false</c> on a time-out (desktop only).</returns>
    private static Task<bool> RunBoundedAsync(Action operation)
    {
#if BROWSER
        operation();
        return Task.FromResult(true);
#else
        return NetworkFileOps.RunAsync(operation, "ExportFile.Write", null, null);
#endif
    }

    /// <summary>
    /// Encodes text as UTF-8 for <see cref="WriteAsync(IStorageFile, byte[])"/>, optionally with the three-byte
    /// byte-order mark Excel needs to open a CSV as UTF-8. With the mark the bytes are exactly what
    /// <c>File.WriteAllText(path, text, new UTF8Encoding(true))</c> wrote; without it, exactly what
    /// <c>File.WriteAllText(path, text)</c> wrote (so JSON files keep their old bytes).
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="withBom">True to prefix the UTF-8 byte-order mark (CSV for Excel); false for BOM-less output (JSON).</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] EncodeUtf8(string text, bool withBom)
    {
        var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom);
        var preamble = encoding.GetPreamble();   // 3 bytes with the mark, none without
        var body     = encoding.GetBytes(text);
        if (preamble.Length == 0) return body;

        var all = new byte[preamble.Length + body.Length];
        preamble.CopyTo(all, 0);
        body.CopyTo(all, preamble.Length);
        return all;
    }

    /// <summary>
    /// Builds the <see cref="ExportWriteOutcome.Failed"/> result for an exception: the plain reason goes to the result,
    /// the exception (type and message) to the local log only.
    /// </summary>
    /// <param name="fileName">The file's name only.</param>
    /// <param name="ex">The exception the write threw.</param>
    /// <returns>A failed result carrying <see cref="PlainReason"/>.</returns>
    private static ExportWriteResult FailedResult(string fileName, Exception ex)
    {
        try { App.Logger.LogInfo($"ExportFileWriter: writing '{fileName}' failed ({ex.GetType().Name}: {ex.Message})"); }
        catch { /* logging must not turn a handled failure into a crash */ }
        return new ExportWriteResult(ExportWriteOutcome.Failed, fileName, PlainReason(ex));
    }

    /// <summary>
    /// The wording for an unsuccessful write; null for <see cref="ExportWriteOutcome.Written"/>. Pure, so the exact
    /// text is unit-tested.
    /// <list type="bullet">
    /// <item><description>Timed out: <c>TermPoint couldn't save {name}: the folder didn't respond. Nothing else was
    /// affected. Try again, or choose a different folder.</c></description></item>
    /// <item><description>Failed: <c>TermPoint couldn't save {name}. {reason}. Nothing else was affected. Try again,
    /// or choose a different folder.</c></description></item>
    /// </list>
    /// </summary>
    /// <param name="result">The write's result.</param>
    /// <returns>The message, or null when the file was written.</returns>
    public static string? MessageFor(ExportWriteResult result) => result.Outcome switch
    {
        ExportWriteOutcome.Written  => null,
        ExportWriteOutcome.TimedOut =>
            $"TermPoint couldn't save {result.FileName}: the folder didn't respond. " +
            "Nothing else was affected. Try again, or choose a different folder.",
        _ =>
            $"TermPoint couldn't save {result.FileName}. {result.Reason ?? GenericReason}. " +
            "Nothing else was affected. Try again, or choose a different folder."
    };

    /// <summary>The reason given when the exception is of no recognised kind.</summary>
    internal const string GenericReason = "The file couldn't be written";

    /// <summary>
    /// Maps a write exception to a plain sentence (no trailing full stop, no path, no exception text).
    /// <list type="bullet">
    /// <item><description><see cref="UnauthorizedAccessException"/> → TermPoint isn't allowed to save in that folder.</description></item>
    /// <item><description><see cref="DirectoryNotFoundException"/> → the folder no longer exists.</description></item>
    /// <item><description><see cref="PathTooLongException"/> → the path is too long.</description></item>
    /// <item><description>An <see cref="IOException"/> whose error code is a sharing/lock violation → the file is open in another program.</description></item>
    /// <item><description>An <see cref="IOException"/> whose error code is disk full → the disk is full.</description></item>
    /// <item><description>Any other <see cref="IOException"/> → the folder reported a problem.</description></item>
    /// <item><description>Anything else → <see cref="GenericReason"/>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="ex">The exception the write threw.</param>
    /// <returns>A short plain reason.</returns>
    internal static string PlainReason(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
                return "TermPoint isn't allowed to save in that folder";
            case DirectoryNotFoundException:
                return "That folder no longer exists";
            case PathTooLongException:
                return "The file's location is too long";
            case IOException io:
                // On Windows the Win32 error is the low 16 bits of HResult: 32 sharing violation, 33 lock violation,
                // 39 (handle disk full) and 112 (disk full). On macOS the errno rides in the same bits: 28 = ENOSPC.
                var code = io.HResult & 0xFFFF;
                if (code is 32 or 33) return "The file is open in another program";
                if (code is 39 or 112 or 28) return "The disk is full";
                return "The folder reported a problem";
            default:
                return GenericReason;
        }
    }
}
