using Avalonia;
using Velopack;

namespace TermPoint;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Handles installer hooks and applies any staged updates.
        // Skipped when running as an MSIX package — the Store manages updates.
        // Extracted to a separate method so the JIT only loads Velopack.dll
        // when this branch actually runs; under MSIX the assembly is never resolved.
        if (!Services.PlatformCapabilities.IsMsixPackage)
            RunVelopackStartup();

        // ── Global exception handlers ────────────────────────────────────────
        // These are last-resort nets. They log the exception and then let the
        // normal crash path proceed (no attempt to keep the app running, since
        // the process state may be corrupt).

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception
                     ?? new Exception($"Non-CLR unhandled exception: {e.ExceptionObject}");
            App.Logger.LogError(ex, "AppDomain.UnhandledException (terminating)", unhandled: true);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            App.Logger.LogError(e.Exception, "TaskScheduler.UnobservedTaskException", unhandled: true);
            e.SetObserved(); // prevent process termination for fire-and-forget tasks
        };

        // ── Did the previous session end unexpectedly? (spec item 7, step 7c) ──
        // A "running" file left by a process that is gone means a crash, a force-quit or a power cut. Reported once,
        // then this process writes its own file, deleted below when Main returns normally.
        var runMarker = new Services.SessionRunMarker(log: message => App.Logger.LogInfo(message));
        ReportSessionsThatEndedUnexpectedly(runMarker);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // A normal exit: this session is not one that ended unexpectedly.
        runMarker.End();

        // ── Shutdown checkpoint #2 ───────────────────────────────────────────
        // Reaching here means the Avalonia dispatcher loop has exited and Main is
        // about to return — i.e. managed code is done. If the OS process
        // (TermPoint.exe) still lingers in Task Manager AFTER this line is logged,
        // the cause is NOT our shutdown logic: it is either the Visual Studio
        // debugger holding the debuggee, or a native handle / non-background
        // thread outside our control. This is the discriminator for the
        // "stray process after shutdown" investigation.
        try
        {
            var threadCount = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
            App.Logger.LogInfo(
                $"[Shutdown] Dispatcher loop exited; Main returning. OS threads still alive: {threadCount}. " +
                "If TermPoint.exe persists past this point, the cause is the debugger or a native handle, not managed shutdown.");
        }
        catch { /* never let shutdown logging throw */ }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunVelopackStartup() => VelopackApp.Build().Run();

    /// <summary>
    /// Starts <paramref name="runMarker"/> and, when it found sessions that ended without exiting normally, writes one
    /// local log line and sends one handled report (<see cref="Exceptions.HandledEvent.PreviousSessionEndedUnexpectedly"/>)
    /// with how many there were and the version the latest one was running. Nothing is shown to the user. Never throws.
    /// </summary>
    /// <param name="runMarker">The marker for this run.</param>
    private static void ReportSessionsThatEndedUnexpectedly(Services.SessionRunMarker runMarker)
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            var ended   = runMarker.Start(Services.SessionRunMarker.ForThisProcess(version));
            if (ended.Count == 0)
                return;

            App.Logger.LogInfo($"[Startup] {ended.Count} previous session(s) ended without exiting normally " +
                               $"(latest was version {ended[^1].AppVersion}).");
            Services.HandledEventReporter.Report(Exceptions.HandledEvent.PreviousSessionEndedUnexpectedly, App.Logger,
                new Dictionary<string, string>
                {
                    ["count"]           = ended.Count.ToString(),
                    ["previousVersion"] = ended[^1].AppVersion,
                });
        }
        catch (Exception)
        {
            // Noticing a crash must never cause one.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .WithInterFont()
            .LogToTrace();
}
