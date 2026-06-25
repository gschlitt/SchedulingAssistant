namespace TermPoint.Services;

/// <summary>
/// TEMPORARY diagnostic helper for the "remote database hangs on the loading curtain
/// after the first-run wizard" investigation.
///
/// <para>The confirmed symptom: after the wizard, the first genuinely-suspending
/// <c>await</c> on a network file op never resumes — its continuation is posted to the
/// UI thread's <see cref="System.Threading.SynchronizationContext"/> but never runs,
/// even though we are still on the UI thread. That points at a swapped or broken
/// <c>SynchronizationContext.Current</c> (a nested modal frame leaving a bad context
/// behind). These crumbs log the context's identity so we can see exactly which wizard
/// step changes it.</para>
///
/// <para>Remove once the root cause is fixed.</para>
/// </summary>
internal static class StartupTrace
{
    /// <summary>
    /// Logs the type and hash of the current <see cref="System.Threading.SynchronizationContext"/>
    /// so a change in identity between two crumbs reveals where it was swapped.
    /// </summary>
    /// <param name="label">A short label identifying the call site.</param>
    public static void Ctx(string label)
    {
        var c = System.Threading.SynchronizationContext.Current;
        var desc = c is null ? "null" : $"{c.GetType().Name}#{c.GetHashCode()}";
        App.Logger.LogInfo(
            $"[startup-trace] SYNCCTX @ {label}: {desc} (tid={Environment.CurrentManagedThreadId})");
    }
}
