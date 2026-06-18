using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

public partial class TagSelectionViewModel : ObservableObject
{
    public SchedulingEnvironmentValue Value { get; }
    [ObservableProperty] private bool _isSelected;

    public TagSelectionViewModel(SchedulingEnvironmentValue value, bool isSelected)
    {
        Value = value;
        _isSelected = isSelected;
    }
}
