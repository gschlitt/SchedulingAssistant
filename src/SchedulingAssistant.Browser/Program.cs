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
