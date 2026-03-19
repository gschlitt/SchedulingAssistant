// Browser (WASM) entry point — intentionally minimal.
//
// WHY THIS IS SO SMALL:
// All application logic lives in the main SchedulingAssistant project.
// This file's only job is to bootstrap the Avalonia WASM runtime and hand off to
// App.OnFrameworkInitializationCompleted, which handles the ISingleViewApplicationLifetime
// branch: calls App.InitializeDemoServices() and sets MainView as the root control.
//
// To switch back to the desktop build, set SchedulingAssistant as the startup project
// in Visual Studio — no code changes are needed.

using Avalonia;
using Avalonia.Browser;
using SchedulingAssistant;
using System.Runtime.Versioning;
using System.Threading.Tasks;

[assembly: SupportedOSPlatform("browser")]

await BuildAvaloniaApp()
    .WithInterFont()
    .StartBrowserAppAsync("out");

static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
                 .LogToTrace();
