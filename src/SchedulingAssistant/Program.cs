using Avalonia;
using SchedulingAssistant.Services;
using System;

namespace SchedulingAssistant;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
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
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
