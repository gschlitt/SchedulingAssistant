using CommunityToolkit.Mvvm.ComponentModel;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// A single checkable item in a filter group (e.g. one instructor, one room).
/// </summary>
public partial class FilterItemViewModel : ViewModelBase
{
    public string Id { get; }
    public string Name { get; }
    public bool IsSentinel { get; init; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isOverlayActive;
    /// <summary>
    /// Controls whether this item's checkbox is interactive. Used for mutual exclusion
    /// between sentinel items ("Not staffed", "Unroomed") and named items in the same
    /// filter dimension: selecting the sentinel disables all named items and vice versa.
    /// Managed by GridFilterViewModel.RefreshInstructorMutualExclusion /
    /// RefreshRoomMutualExclusion. Defaults to true (all items enabled).
    /// </summary>
    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>
    /// Tags only: whether the tag is an access watch (the eye in the Tags popup is lit). Set from the tag record
    /// when the option list is built, and flipped by <see cref="GridFilterViewModel.ToggleTagWatchCommand"/>. Not
    /// a filter state: changing it never reloads the grid by itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchTip))]
    private bool _isWatched;

    /// <summary>The eye's tooltip: what a click does (approved wording).</summary>
    // approved wording
    public string WatchTip => IsWatched ? "Watched: click to stop watching" : "Watch this tag";

    public FilterItemViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
