using System.Reflection;
using TermPoint;
using TermPoint.Services;

namespace NetworkProbe;

/// <summary>What <see cref="LoggerRig.Install"/> did, for the console and the report.</summary>
/// <param name="OriginalLoggerType">The type of the logger <c>App</c> created on its own.</param>
/// <param name="BugsnagWasAttached">True when that logger held a live BugSnag client.</param>
/// <param name="BugsnagDetached">True when the logger no longer has a BugSnag client.</param>
/// <param name="LogDirectory">The folder the original logger would now write to (the probe's local temp folder).</param>
/// <param name="DetachedHandlers">The process-wide exception handlers the BugSnag client had installed, now removed.</param>
/// <param name="RemainingBugsnagHandlers">Handlers of BugSnag's that could NOT be removed (should be empty).</param>
internal sealed record LoggerRigStatus(
    string OriginalLoggerType,
    bool BugsnagWasAttached,
    bool BugsnagDetached,
    string LogDirectory,
    IReadOnlyList<string> DetachedHandlers,
    IReadOnlyList<string> RemainingBugsnagHandlers)
{
    /// <summary>True when every safeguard is in place: no BugSnag client, no BugSnag handler left.</summary>
    public bool Safe => BugsnagDetached && RemainingBugsnagHandlers.Count == 0;
}

/// <summary>
/// Test-rig plumbing (spec section 4, "Never write TermPoint's real log or report to BugSnag"): keeps TermPoint's
/// static <c>App</c> logger away from the real log file and from BugSnag, and swaps in the probe's recording logger.
/// It mirrors <c>KeepAppLoggerAwayFromRealFilesAndBugsnag</c> in <c>src/TermPoint.Tests/RecordingAppLogger.cs</c>,
/// and uses reflection for the same two internal members that file reaches, so nothing under <c>src/</c> has to change:
/// <c>FileAppLogger.LogDirectoryOverride</c> (internal) and <c>App</c>'s logger backing field (the setter is private).
///
/// <para><b>One thing the test project does not do, and the probe must:</b> reading <c>App.Logger</c> runs
/// <c>App</c>'s static initialisers, which build a BugSnag client that hooks three PROCESS-WIDE events
/// (<c>AppDomain.UnhandledException</c>, <c>TaskScheduler.UnobservedTaskException</c>, <c>AppDomain.ProcessExit</c>).
/// Clearing the logger's <c>BugsnagClient</c> does not unhook them, so a crash or an unobserved task fault in the
/// probe would still be reported to BugSnag (in a Release build under the "production" release stage). So the
/// handlers the client added are found by comparing the event lists before and after, and removed.</para>
/// </summary>
internal static class LoggerRig
{
    /// <summary>Flags that find private and public, static and instance members.</summary>
    private const BindingFlags Any =
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// Redirects, detaches and replaces, in that order, and then checks the result. Must be called before the first
    /// <see cref="NetworkFileOps"/> call.
    /// </summary>
    /// <param name="logger">The probe's recording logger, which becomes <c>App.Logger</c>.</param>
    /// <param name="logDirectory">A local temp folder for any file logger that is created anywhere in this process.</param>
    /// <returns>What was done and whether every safeguard holds.</returns>
    /// <exception cref="InvalidOperationException">
    /// A member the rig needs is missing (TermPoint was changed), or the replacement did not take effect.
    /// </exception>
    public static LoggerRigStatus Install(RecordingProbeLogger logger, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        // 1. Point any FileAppLogger at the probe's temp folder BEFORE App's static initialisers run, so even a line
        //    they might log lands there and not in %AppData%\TermPoint\Logs.
        var overrideProperty = typeof(FileAppLogger).GetProperty("LogDirectoryOverride", Any)
            ?? throw new InvalidOperationException("FileAppLogger.LogDirectoryOverride was not found; TermPoint changed.");
        overrideProperty.SetValue(null, logDirectory);

        // 2. Remember who is hooked into the process-wide events, then touch App.Logger. This is what runs App's
        //    static initialisers (WriteLockService, DatabaseInstanceGuard, CheckoutService, the BugSnag client).
        var before = ProcessEventHandlers.Snapshot();
        var original = App.Logger;
        var after = ProcessEventHandlers.Snapshot();

        // 3. Cut the BugSnag client off the logger.
        var bugsnagWasAttached = false;
        if (original is FileAppLogger fileLogger)
        {
            bugsnagWasAttached = fileLogger.BugsnagClient is not null;
            fileLogger.BugsnagClient = null;
        }

        // 4. Unhook the handlers the BugSnag client installed (everything new that belongs to the Bugsnag assembly).
        var detached = ProcessEventHandlers.RemoveAdded(before, after, d => IsBugsnag(d));

        // 5. Replace App.Logger through its compiler-generated backing field, exactly as the tests do.
        var field = typeof(App).GetField("<Logger>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("App's logger backing field was not found; TermPoint changed.");
        field.SetValue(null, logger);

        // 6. Verify: the swap took, the client is gone, no BugSnag handler remains anywhere.
        if (!ReferenceEquals(App.Logger, logger))
            throw new InvalidOperationException("Replacing App.Logger did not take effect; refusing to run.");
        var remaining = ProcessEventHandlers.Snapshot().Where(h => IsBugsnag(h.Handler)).Select(h => h.Describe()).ToList();
        var detachedNow = original is not FileAppLogger { BugsnagClient: not null };

        return new LoggerRigStatus(
            original.GetType().Name, bugsnagWasAttached, detachedNow, logDirectory,
            detached.Select(h => h.Describe()).ToList(), remaining);
    }

    /// <summary>True when a delegate's method belongs to the BugSnag assembly.</summary>
    /// <param name="handler">An event handler.</param>
    /// <returns>True for BugSnag's own handlers.</returns>
    private static bool IsBugsnag(Delegate handler)
        => handler.Method.DeclaringType?.Assembly.GetName().Name?.StartsWith("Bugsnag", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>One event handler found on a process-wide event, and the means to remove it again.</summary>
/// <param name="EventName">Which event, for messages.</param>
/// <param name="Handler">The handler delegate.</param>
/// <param name="Remove">Unsubscribes <paramref name="Handler"/> through the event's public remove accessor.</param>
internal sealed record ProcessEventHandler(string EventName, Delegate Handler, Action<Delegate> Remove)
{
    /// <summary>Describes the handler for a message, for example <c>TaskScheduler.UnobservedTaskException: Bugsnag.UnhandledException.TaskScheduler_UnobservedTaskException</c>.</summary>
    /// <returns>The description.</returns>
    public string Describe() => $"{EventName}: {Handler.Method.DeclaringType?.FullName}.{Handler.Method.Name}";
}

/// <summary>
/// Lists and removes the handlers subscribed to the three process-wide events a crash reporter hooks. The runtime
/// keeps each event's handlers in a private field, so they are read by reflection; they are removed through the
/// events' public <c>-=</c> accessors, so the runtime's own bookkeeping stays correct.
/// </summary>
internal static class ProcessEventHandlers
{
    /// <summary>Flags that find private and public, static and instance fields.</summary>
    private const BindingFlags Any =
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// Reads the handlers now subscribed to <c>AppDomain.UnhandledException</c>,
    /// <c>TaskScheduler.UnobservedTaskException</c> and <c>AppDomain.ProcessExit</c>.
    /// </summary>
    /// <returns>One entry per handler; an event whose storage cannot be found contributes nothing.</returns>
    public static IReadOnlyList<ProcessEventHandler> Snapshot()
    {
        var list = new List<ProcessEventHandler>();

        // AppDomain.UnhandledException is stored on AppContext in current runtimes (and on AppDomain in older ones).
        AddFrom(list, "AppDomain.UnhandledException", typeof(AppContext), AppDomain.CurrentDomain, "UnhandledException",
            d => AppDomain.CurrentDomain.UnhandledException -= (UnhandledExceptionEventHandler)d);
        AddFrom(list, "AppDomain.UnhandledException", typeof(AppDomain), AppDomain.CurrentDomain, "UnhandledException",
            d => AppDomain.CurrentDomain.UnhandledException -= (UnhandledExceptionEventHandler)d);
        AddFrom(list, "TaskScheduler.UnobservedTaskException", typeof(TaskScheduler), null, "UnobservedTaskException",
            d => TaskScheduler.UnobservedTaskException -= (EventHandler<UnobservedTaskExceptionEventArgs>)d);
        AddFrom(list, "AppDomain.ProcessExit", typeof(AppDomain), AppDomain.CurrentDomain, "ProcessExit",
            d => AppDomain.CurrentDomain.ProcessExit -= (EventHandler)d);
        return list;
    }

    /// <summary>
    /// Removes the handlers that appeared between two snapshots and that the filter accepts.
    /// </summary>
    /// <param name="before">The snapshot taken before the code under suspicion ran.</param>
    /// <param name="after">The snapshot taken after it ran.</param>
    /// <param name="accept">Which of the new handlers to remove.</param>
    /// <returns>The handlers that were removed.</returns>
    public static IReadOnlyList<ProcessEventHandler> RemoveAdded(
        IReadOnlyList<ProcessEventHandler> before, IReadOnlyList<ProcessEventHandler> after, Func<Delegate, bool> accept)
    {
        var removed = new List<ProcessEventHandler>();
        foreach (var handler in after)
        {
            var alreadyThere = before.Any(b => b.EventName == handler.EventName && Equals(b.Handler, handler.Handler));
            if (alreadyThere || !accept(handler.Handler)) continue;
            handler.Remove(handler.Handler);
            removed.Add(handler);
        }
        return removed;
    }

    /// <summary>
    /// Adds the handlers stored in one named field, if that field exists on the type.
    /// </summary>
    /// <param name="list">Where to add.</param>
    /// <param name="eventName">The event's name, for messages.</param>
    /// <param name="owner">The type that stores the handlers.</param>
    /// <param name="instance">The instance for an instance field, or null for a static one.</param>
    /// <param name="fieldName">The storage field's name.</param>
    /// <param name="remove">How to unsubscribe one handler.</param>
    private static void AddFrom(
        List<ProcessEventHandler> list, string eventName, Type owner, object? instance, string fieldName, Action<Delegate> remove)
    {
        var field = owner.GetField(fieldName, Any);
        if (field is null || !typeof(MulticastDelegate).IsAssignableFrom(field.FieldType)) return;
        var value = field.GetValue(field.IsStatic ? null : instance) as MulticastDelegate;
        if (value is null) return;
        foreach (var handler in value.GetInvocationList())
            list.Add(new ProcessEventHandler(eventName, handler, remove));
    }
}
