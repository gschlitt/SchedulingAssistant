using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class TagSelectionViewModel : ObservableObject
{
    public SectionPropertyValue Value { get; }
    [ObservableProperty] private bool _isSelected;

    public TagSelectionViewModel(SectionPropertyValue value, bool isSelected)
    {
        Value = value;
        _isSelected = isSelected;
    }
}
