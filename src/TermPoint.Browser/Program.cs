using Avalonia;
using Avalonia.Browser;
using TermPoint;
using System.Runtime.Versioning;
using System.Threading.Tasks;

[assembly: SupportedOSPlatform("browser")]

await BuildAvaloniaApp()
    .WithInterFont()
    .StartBrowserAppAsync("out");

static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
                 .LogToTrace();
