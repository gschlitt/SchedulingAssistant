namespace NetworkProbe;

/// <summary>
/// Console output that can never stop the run. Writing to a console that has gone away (the window was closed, the output
/// was piped into something that ended) throws <see cref="IOException"/>; if that escaped, it could skip the very code that
/// stops clumsy, restores the network and deletes the probe's folders. Every progress line, summary line and report notice
/// therefore goes through here, and a failed write is simply dropped.
/// </summary>
internal static class ConsoleOut
{
    /// <summary>
    /// Writes a line to the console, ignoring any failure to do so.
    /// </summary>
    /// <param name="text">The line; empty for a blank line.</param>
    public static void Line(string text = "")
    {
        try { Console.WriteLine(text); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            // The console is gone. The run carries on: restoring the network and cleaning up matter more than the text.
        }
    }
}
