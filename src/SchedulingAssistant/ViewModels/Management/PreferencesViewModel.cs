namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Preferences panel in the Configuration flyout.
/// Exposes user-facing preferences such as display and UI options.
/// </summary>
public partial class PreferencesViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Preferences";
}
