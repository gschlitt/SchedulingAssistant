namespace NetworkProbe;

/// <summary>
/// File-system work done by the RIG itself (creating test files, uploading the large file, deleting folders, polling
/// for the share), as opposed to the calls under test. A plain <c>File.*</c> call on a dead share can block for tens of
/// seconds, and the rig must never hang on that, so every rig-side call is run on a thread-pool thread and abandoned
/// if it does not finish in time. This is deliberately NOT <c>NetworkFileOps</c>: the rig must not use the code it is
/// measuring, and its calls must not add lines to the log a case is judged on.
/// </summary>
internal static class RigIo
{
    /// <summary>Normal wait for a small rig-side call made while the network is up.</summary>
    public static readonly TimeSpan Short = TimeSpan.FromSeconds(30);

    /// <summary>Wait for uploading the large test file while the network is up.</summary>
    public static readonly TimeSpan Upload = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Runs a call and returns its result, or gives up after a deadline.
    /// </summary>
    /// <typeparam name="T">The call's result type.</typeparam>
    /// <param name="work">The call.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="what">What the call does, in plain words, for the timeout message.</param>
    /// <returns>What the call returned.</returns>
    /// <exception cref="TimeoutException">The call did not finish in time; it is abandoned (its late failure is observed).</exception>
    /// <exception cref="Exception">Whatever the call itself throws.</exception>
    public static T Run<T>(Func<T> work, TimeSpan timeout, string what)
    {
        var task = Task.Run(work);
        if (!task.Wait(timeout))
        {
            Abandon(task);
            throw new TimeoutException($"{what} did not finish within {timeout.TotalSeconds:F0} s.");
        }
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs a call that returns nothing, or gives up after a deadline.
    /// </summary>
    /// <param name="work">The call.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="what">What the call does, in plain words, for the timeout message.</param>
    /// <exception cref="TimeoutException">The call did not finish in time; it is abandoned.</exception>
    /// <exception cref="Exception">Whatever the call itself throws.</exception>
    public static void Run(Action work, TimeSpan timeout, string what)
        => Run(() => { work(); return true; }, timeout, what);

    /// <summary>
    /// Runs a clean-up call and swallows every failure, including a timeout.
    /// </summary>
    /// <param name="work">The call.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>True when the call finished without an exception.</returns>
    public static bool TryRun(Action work, TimeSpan timeout)
    {
        try { Run(work, timeout, "clean-up"); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Reads a file's length with a plain <c>FileInfo</c>, giving up after a deadline. Used before a mid-operation cut
    /// to see that a copy's destination has started to grow.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The length, or null when the file does not exist yet, the call failed or timed out.</returns>
    public static long? TryGetLength(string path, TimeSpan timeout)
    {
        try
        {
            return Run(() =>
            {
                var info = new FileInfo(path);
                return info.Exists ? (long?)info.Length : null;
            }, timeout, "reading a file length");
        }
        catch { return null; }
    }

    /// <summary>
    /// Makes sure the late outcome of an abandoned task is observed, so it can never surface as an unobserved task
    /// exception (which would also be a process-wide event BugSnag listens to).
    /// </summary>
    /// <param name="task">The abandoned task.</param>
    public static void Abandon(Task task)
        => _ = task.ContinueWith(
            t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
