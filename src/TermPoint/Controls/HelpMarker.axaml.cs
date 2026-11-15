using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Services;

namespace TermPoint.Controls;

/// <summary>
/// A small blue "?" badge that opens a Help article when clicked (for example beside the Filters and Overlays
/// headings of the Schedule View). The look is in HelpMarker.axaml; this class only holds the article name and the
/// command that opens it.
/// <para>
/// Usage (with <c>xmlns:controls="using:TermPoint.Controls"</c>):
/// <c>&lt;controls:HelpMarker Article="schedule-grid-filters.html" ToolTip.Tip="How filters combine (opens Help)" /&gt;</c>
/// </para>
/// </summary>
public partial class HelpMarker : UserControl
{
    /// <summary>
    /// The Help article to open: a file name in the Help folder, or a full "http…" URL. When empty, clicking does
    /// nothing.
    /// </summary>
    public static readonly StyledProperty<string?> ArticleProperty =
        AvaloniaProperty.Register<HelpMarker, string?>(nameof(Article));

    /// <summary>Gets or sets the Help article this marker opens (see <see cref="ArticleProperty"/>).</summary>
    public string? Article
    {
        get => GetValue(ArticleProperty);
        set => SetValue(ArticleProperty, value);
    }

    /// <summary>
    /// Opens <see cref="Article"/> in the user's browser, through <see cref="HelpArticles.Open"/>. Bound to the
    /// marker's button in HelpMarker.axaml.
    /// </summary>
    public ICommand OpenCommand { get; }

    /// <summary>Creates the marker and its open command.</summary>
    public HelpMarker()
    {
        OpenCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(Article))
                HelpArticles.Open(Article);
        });
        InitializeComponent();
    }
}
