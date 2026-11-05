using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

namespace TileWidthProbe;

/// <summary>
/// Boots Avalonia without the app's startup flow (no wizard, no database chooser, no logger files) and gives the
/// application the REAL resources from <c>src/TermPoint/App.axaml</c>, so the real ScheduleGridView reads the same
/// colours, layout numbers and font sizes it does in the app.
/// </summary>
static class ProbeBootstrap
{
    /// <summary>Avalonia's XAML namespace, used when picking App.axaml apart.</summary>
    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    /// <summary>
    /// Starts Avalonia on the calling thread (Win32 + Skia on Windows), adds the app's theme, and installs the
    /// app's resources.
    /// </summary>
    /// <param name="appAxamlPath">Path of <c>src/TermPoint/App.axaml</c>.</param>
    public static void Start(string appAxamlPath)
    {
        AppBuilder.Configure<Application>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        var app = Application.Current!;
        app.RequestedThemeVariant = ThemeVariant.Light;
        app.Styles.Add(new SimpleTheme());

        // The app's one global style that reaches the grid canvas: TextBlocks default to the primary text colour
        // (the day names rely on it). The rest of App.axaml's styles style buttons, menus and so on, none of
        // which the canvas uses.
        app.Resources = LoadAppResources(appAxamlPath);
        var textStyle = new Style(x => x.OfType<TextBlock>());
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            app.Resources.TryGetResource("TextPrimary", null, out var tp) ? tp : Brushes.Black));
        app.Styles.Add(textStyle);
    }

    /// <summary>
    /// Builds the application's resource dictionary from the real App.axaml: its Application.Resources
    /// ResourceDictionary (merged AppColors/AppLayout/Icons/TourContent, font sizes, converters) is lifted out,
    /// given the same namespace declarations, and loaded with the runtime XAML loader. Nothing is copied by hand.
    /// </summary>
    /// <param name="appAxamlPath">Path of App.axaml.</param>
    /// <returns>The resource dictionary to install as <c>Application.Resources</c>.</returns>
    private static ResourceDictionary LoadAppResources(string appAxamlPath)
    {
        var root = XDocument.Load(appAxamlPath).Root!;
        var dict = new XElement(root.Element(Av + "Application.Resources")!.Element(Av + "ResourceDictionary")!);

        // Carry the root's xmlns declarations (x:, conv:, local:) onto the lifted element.
        foreach (var ns in root.Attributes().Where(a => a.IsNamespaceDeclaration))
            if (dict.Attribute(ns.Name) is null && ns.Name != "xmlns")
                dict.Add(new XAttribute(ns.Name, ns.Value));

        var loaded = AvaloniaRuntimeXamlLoader.Load(dict.ToString(), typeof(TermPoint.App).Assembly);
        return (ResourceDictionary)loaded;
    }
}
