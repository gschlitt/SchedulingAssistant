using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class ResourceSelectionViewModel : ObservableObject
{
    public SchedulingEnvironmentValue Value { get; }
    [ObservableProperty] private bool _isSelected;

    public ResourceSelectionViewModel(SchedulingEnvironmentValue value, bool isSelected)
    {
        Value = value;
        _isSelected = isSelected;
    }
}
