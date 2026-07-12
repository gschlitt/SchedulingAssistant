using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// Wraps a single <see cref="ProgramWatch"/> for display in the Access panel watch list.
/// Changes to <see cref="Name"/> and <see cref="IsEnabled"/> are written back through
/// the parent <see cref="AccessPanelViewModel"/>.
/// </summary>
public partial class ProgramWatchItemViewModel : ObservableObject
{
    private readonly Action<ProgramWatchItemViewModel> _onChanged;
    private readonly Action<ProgramWatchItemViewModel> _onDeleteRequested;

    /// <summary>The underlying watch model.</summary>
    public ProgramWatch Watch { get; }

    /// <summary>User-editable display name.</summary>
    [ObservableProperty] private string _name;

    /// <summary>On/off toggle — disabled watches produce no conflict indications.</summary>
    [ObservableProperty] private bool _isEnabled;

    /// <summary>Compact summary of the watch definition, e.g. "Tags: upper-level + BSc" or "Courses: 3".</summary>
    [ObservableProperty] private string _modeSummary;

    /// <summary>Number of conflicts detected for this watch (updated after each grid reload).</summary>
    [ObservableProperty] private int _conflictCount;

    /// <summary>Display text for the conflict count, e.g. "3 conflicts". Empty when zero.</summary>
    [ObservableProperty] private string _conflictText = string.Empty;

    /// <param name="watch">The underlying watch model.</param>
    /// <param name="modeSummary">Pre-formatted mode summary string.</param>
    /// <param name="onChanged">Callback when Name or IsEnabled changes (triggers save + grid reload).</param>
    /// <param name="onDeleteRequested">Callback when the user clicks Delete.</param>
    public ProgramWatchItemViewModel(
        ProgramWatch watch,
        string modeSummary,
        Action<ProgramWatchItemViewModel> onChanged,
        Action<ProgramWatchItemViewModel> onDeleteRequested)
    {
        Watch = watch;
        _name = watch.Name;
        _isEnabled = watch.IsEnabled;
        _modeSummary = modeSummary;
        _onChanged = onChanged;
        _onDeleteRequested = onDeleteRequested;
    }

    partial void OnNameChanged(string value)
    {
        Watch.Name = value;
        _onChanged(this);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        Watch.IsEnabled = value;
        _onChanged(this);
    }

    partial void OnConflictCountChanged(int value)
    {
        ConflictText = value switch
        {
            0 => string.Empty,
            1 => "1 conflict",
            _ => $"{value} conflicts"
        };
    }

    /// <summary>Invoked by the Delete button in the watch list.</summary>
    [RelayCommand]
    private void RequestDelete() => _onDeleteRequested(this);
}
