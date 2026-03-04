using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// A single checkable item in a filter group (e.g. one instructor, one room).
/// </summary>
public partial class FilterItemViewModel : ViewModelBase
{
    public string Id { get; }
    public string Name { get; }

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

    public FilterItemViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
