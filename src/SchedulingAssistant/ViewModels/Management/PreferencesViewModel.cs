using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Preferences panel in the Configuration flyout.
/// Exposes user-facing preferences that are persisted in user settings
/// (not in the database), so they are per-machine rather than per-schedule.
/// </summary>
public class PreferencesViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Preferences";

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
