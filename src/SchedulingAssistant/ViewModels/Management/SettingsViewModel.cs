using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private bool _includeSaturday;

    public SettingsViewModel()
    {
        _includeSaturday = AppSettings.Load().IncludeSaturday;
    }

    partial void OnIncludeSaturdayChanged(bool value)
    {
        var settings = AppSettings.Load();
        settings.IncludeSaturday = value;
        settings.Save();
    }
}
