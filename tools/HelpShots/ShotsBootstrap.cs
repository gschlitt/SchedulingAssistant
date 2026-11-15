using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

namespace HelpShots;

/// <summary>
/// Boots Avalonia without the app's startup flow (no wizard, no database chooser, no log files) and installs the REAL
/// resources AND styles from <c>src/TermPoint/App.axaml</c>, so the views drawn for Help look exactly as in the app.
/// <para>
/// <c>TermPoint.App</c> itself is not instantiated: its framework-initialised hook starts the whole app. Instead the
/// two sections of App.axaml are lifted out and loaded with the runtime XAML loader (the same technique as
/// tools/TileWidthProbe, extended to the styles, which the filter bar's checkboxes and buttons depend on).
/// </para>
/// </summary>
static class ShotsBootstrap
{
    /// <summary>Avalonia's XAML namespace, used when picking App.axaml apart.</summary>
    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    /// <summary>Starts Avalonia on the calling thread and installs the app's resources and styles.</summary>
    /// <param name="appAxamlPath">Path of <c>src/TermPoint/App.axaml</c>.</param>
    /// <exception cref="InvalidOperationException">App.axaml lacks its Resources or Styles section.</exception>
    public static void Start(string appAxamlPath)
    {
        AppBuilder.Configure<Application>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        var app = Application.Current!;
        app.RequestedThemeVariant = ThemeVariant.Light;

        var root = XDocument.Load(appAxamlPath).Root!;
        var resources = root.Element(Av + "Application.Resources")?.Element(Av + "ResourceDictionary")
                        ?? throw new InvalidOperationException("App.axaml has no Application.Resources/ResourceDictionary.");
        var styles = root.Element(Av + "Application.Styles")
                     ?? throw new InvalidOperationException("App.axaml has no Application.Styles.");

        app.Resources = (ResourceDictionary)Load(root, new XElement(resources));

        // Application.Styles' children become one <Styles> element . Styles for the
        // DataGrid and the colour picker are left out: the runtime loader cannot resolve those controls' types, and
        // nothing drawn for Help uses them.
        static bool Unneeded(XElement e) =>
            ((string?)e.Attribute("Selector") ?? (string?)e.Attribute("Source") ?? "") is var s
            && (s.Contains("DataGrid") || s.Contains("ColorView") || s.Contains("ColorPicker"))
            || e.Name.LocalName == "SimpleTheme";
        var stylesElement = new XElement(Av + "Styles", styles.Elements().Where(e => !Unneeded(e)));
        // SimpleTheme is added from code (the runtime loader cannot resolve its type); it goes first, as in App.axaml.
        app.Styles.Add(new SimpleTheme());
        app.Styles.Add((Styles)Load(root, stylesElement));
    }

    /// <summary>
    /// Loads one lifted element of App.axaml, after carrying over the root's namespace declarations (x:, conv:, local:)
    /// so prefixed names still resolve.
    /// </summary>
    /// <param name="root">App.axaml's root element (the source of the namespace declarations).</param>
    /// <param name="element">The lifted element.</param>
    /// <returns>The loaded object.</returns>
    private static object Load(XElement root, XElement element)
    {
        foreach (var ns in root.Attributes().Where(a => a.IsNamespaceDeclaration))
            if (element.Attribute(ns.Name) is null && ns.Name != "xmlns")
                element.Add(new XAttribute(ns.Name, ns.Value));
        return AvaloniaRuntimeXamlLoader.Load(element.ToString(), typeof(TermPoint.App).Assembly);
    }
}
