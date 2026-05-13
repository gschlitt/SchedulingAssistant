using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Preferences panel in the Configuration flyout.
/// Exposes user-facing preferences that are persisted in user settings
/// (not in the database), so they are per-machine rather than per-schedule.
/// </summary>
public partial class PreferencesViewModel : ViewModelBase
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
    /// True when the filter-pass behavior is set to highlight matched sections.
    /// Mutually exclusive with <see cref="FilterExpandsSection"/>.
    /// </summary>
    [ObservableProperty] private bool _filterHighlightsSection = true;

    /// <summary>
    /// True when the filter-pass behavior is set to expand matched sections
    /// (collapsing all others).  Mutually exclusive with <see cref="FilterHighlightsSection"/>.
    /// </summary>
    [ObservableProperty] private bool _filterExpandsSection;
}
