using System.Diagnostics;

namespace NetworkProbe;

/// <summary>
/// The result of one file call made by the runner on its own computer, timed on the runner's stopwatch: whether it
/// worked, what it returned, or which exception it threw (type and HResult are what the experiment reports).
/// </summary>
/// <param name="Ok">True when the call returned normally.</param>
/// <param name="Detail">What the call returned, as text (empty on failure).</param>
/// <param name="Error">The exception, or null on success.</param>
/// <param name="Ms">How long the call took, in milliseconds.</param>
internal readonly record struct LocalCall(bool Ok, string Detail, Exception? Error, double Ms)
{
    /// <summary>The exception's HResult, or 0 when the call worked.</summary>
    public int HResult => Error?.HResult ?? 0;

    /// <summary>The outcome in a few words: <c>ok</c> or <c>IOException 0x80070050</c>.</summary>
    public string Text => Ok ? "ok" : $"{Error!.GetType().Name} {ShareProtocol.HResultText(Error.HResult)}";

    /// <summary>True when the call failed because the file or its folder was not found.</summary>
    public bool IsNotFound => Error is FileNotFoundException or DirectoryNotFoundException;

    /// <summary>
    /// True when the call failed with Windows' "file exists" (error 80) or "already exists" (error 183).
    /// </summary>
    public bool IsAlreadyExists => Error is IOException && Error is not (FileNotFoundException or DirectoryNotFoundException)
                                   && (Error.HResult & 0xFFFF) is 80 or 183;

    /// <summary>
    /// Runs an operation, timing it and capturing any I/O failure.
    /// </summary>
    /// <param name="operation">The file call; returns a short description of what it returned.</param>
    /// <returns>The timed result. Only I/O-type exceptions are captured; anything else (a bug) propagates.</returns>
    public static LocalCall Timed(Func<string> operation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = operation();
            return new LocalCall(true, detail, null, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LocalCall(false, "", ex, sw.Elapsed.TotalMilliseconds);
        }
    }
}
