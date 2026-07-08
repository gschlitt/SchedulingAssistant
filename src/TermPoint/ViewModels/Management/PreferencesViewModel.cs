using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// ViewModel for the Preferences panel in the Configuration flyout.
/// Exposes user-facing preferences that are persisted in user settings
/// (not in the database), so they are per-machine rather than per-schedule.
/// </summary>
public class PreferencesViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Preferences";

    // ── Startup ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="AppSettings.AlwaysOpenMostRecentDatabase"/>.
    /// When true, the database chooser is skipped at startup and the most recently
    /// used database is opened directly. Persists immediately on change.
    /// </summary>
    public bool AlwaysOpenMostRecentDatabase
    {
        get => AppSettings.Current.AlwaysOpenMostRecentDatabase;
        set
        {
            if (AppSettings.Current.AlwaysOpenMostRecentDatabase == value) return;
            AppSettings.Current.AlwaysOpenMostRecentDatabase = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    // ── Section View ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="AppSettings.OpenWithAllSectionsCollapsed"/>.
    /// Reads from and writes to the settings singleton; persists immediately on change.
    /// </summary>
    public bool OpenWithAllSectionsCollapsed
    {
        get => AppSettings.Current.OpenWithAllSectionsCollapsed;
        set
        {
            if (AppSettings.Current.OpenWithAllSectionsCollapsed == value) return;
            AppSettings.Current.OpenWithAllSectionsCollapsed = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True when the filter-pass behavior is <see cref="FilterPassBehavior.Highlight"/>.
    /// Mutually exclusive with <see cref="FilterExpandsSection"/>.
    /// </summary>
    public bool FilterHighlightsSection
    {
        get => AppSettings.Current.FilterPassBehavior == FilterPassBehavior.Highlight;
        set { if (value) SetFilterPassBehavior(FilterPassBehavior.Highlight); }
    }

    /// <summary>
    /// True when the filter-pass behavior is <see cref="FilterPassBehavior.Expand"/>.
    /// Mutually exclusive with <see cref="FilterHighlightsSection"/>.
    /// </summary>
    public bool FilterExpandsSection
    {
        get => AppSettings.Current.FilterPassBehavior == FilterPassBehavior.Expand;
        set { if (value) SetFilterPassBehavior(FilterPassBehavior.Expand); }
    }

    private void SetFilterPassBehavior(FilterPassBehavior behavior)
    {
        if (AppSettings.Current.FilterPassBehavior == behavior) return;
        AppSettings.Current.FilterPassBehavior = behavior;
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(FilterHighlightsSection));
        OnPropertyChanged(nameof(FilterExpandsSection));
    }

    // ── Access Mode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="AppSettings.OpenInReaderMode"/>. When true, the app opens as a
    /// read-only observer that never acquires the write lock (so it can never block a writer).
    /// The change takes effect on the next app start. Persists immediately on change.
    /// </summary>
    public bool OpenInReaderMode
    {
        get => AppSettings.Current.OpenInReaderMode;
        set
        {
            if (AppSettings.Current.OpenInReaderMode == value) return;
            AppSettings.Current.OpenInReaderMode = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    // ── Schedule View ─────────────────────────────────────────────────────────

    /// <summary>Font size options shown in the tile font size selector.</summary>
    public static IReadOnlyList<double> FontSizeOptions => ScheduleGridViewModel.FontSizeOptions;

    /// <summary>
    /// Default tile font size for the Schedule Grid. Persisted to user settings.
    /// Applied on startup; the user may override per-session via the in-grid selector.
    /// </summary>
    public double TileFontSize
    {
        get => AppSettings.Current.TileFontSize;
        set
        {
            if (AppSettings.Current.TileFontSize == value) return;
            AppSettings.Current.TileFontSize = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }
}
