using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>A checkable day row used in the block-pattern editor.</summary>
public partial class DayCheckViewModel : ViewModelBase
{
    public int Day { get; }
    public string DayName { get; }

    [ObservableProperty] private bool _isChecked;

    public DayCheckViewModel(int day, string dayName, bool isChecked = false)
    {
        Day = day;
        DayName = dayName;
        _isChecked = isChecked;
    }
}
