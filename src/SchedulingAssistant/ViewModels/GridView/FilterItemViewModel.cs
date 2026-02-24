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

    public FilterItemViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
