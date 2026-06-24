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
        if (!Services.PlatformCapabilities.IsMsixPackage)
            VelopackApp.Build().Run();

        // ── Avalonia bug #19892: suppress PointToScreen crash ────────────────
        // AutoCompleteBox dropdown click triggers a delayed crash from
        // PlatformImpl-null + IsPointerEventWithinBounds → PointToScreen.
        // The selection has already committed, so the exception is cosmetic.
        // Harmony patches PointToScreen to swallow this specific exception.
        AvaloniaPatches.Apply();

        // ── Global exception handlers ────────────────────────────────────────
        // These are last-resort nets. They log the exception and then let the
        // normal crash path proceed (no attempt to keep the app running, since
        // the process state may be corrupt).

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception
                     ?? new Exception($"Non-CLR unhandled exception: {e.ExceptionObject}");
            App.Logger.LogError(ex, "AppDomain.UnhandledException (terminating)");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            App.Logger.LogError(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved(); // prevent process termination for fire-and-forget tasks
        };
         BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
