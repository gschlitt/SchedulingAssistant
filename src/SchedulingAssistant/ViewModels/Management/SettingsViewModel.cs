using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private bool _includeSaturday;

    public SettingsViewModel()
    {
        _includeSaturday = AppSettings.Current.IncludeSaturday;
    }

    partial void OnIncludeSaturdayChanged(bool value)
    {
        var settings = AppSettings.Current;
        settings.IncludeSaturday = value;
        settings.Save();
    }
}
