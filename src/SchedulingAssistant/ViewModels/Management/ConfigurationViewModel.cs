using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Configuration flyout hub.
/// Hosts a left-sidebar list of configuration categories; selecting one displays
/// its view in the right panel via the ViewLocator.
/// </summary>
public partial class ConfigurationViewModel : ViewModelBase, IDismissableEditor
{
    /// <summary>Ordered list of configuration category ViewModels shown in the left sidebar.</summary>
    public ObservableCollection<ViewModelBase> Categories { get; }

#if !BROWSER
    /// <summary>
    /// The Save &amp; Backup category VM, exposed so the caller can wire
    /// <see cref="SaveAndBackupViewModel.RestoreCallback"/> before opening the flyout.
    /// </summary>
    public SaveAndBackupViewModel SaveAndBackupVm { get; }
#endif

    [ObservableProperty]
    private ViewModelBase? _selectedCategory;

    /// <param name="schedulingVm">Legal start times / block lengths category.</param>
    /// <param name="blockPatternsVm">Block patterns category.</param>
    /// <param name="sectionCodePatternsVm">Section code patterns category.</param>
    /// <param name="academicUnitsVm">Academic unit category.</param>
    /// <param name="saveAndBackupVm">Save &amp; Backup category.</param>
    public ConfigurationViewModel(
        LegalStartTimeListViewModel schedulingVm,
        BlockPatternListViewModel blockPatternsVm,
        SectionCodePatternListViewModel sectionCodePatternsVm,
        AcademicUnitListViewModel academicUnitsVm
#if !BROWSER
        , SaveAndBackupViewModel saveAndBackupVm
#endif
    )
    {
#if !BROWSER
        SaveAndBackupVm = saveAndBackupVm;
#endif

        Categories = new ObservableCollection<ViewModelBase>
        {
            schedulingVm,
            blockPatternsVm,
            sectionCodePatternsVm,
            academicUnitsVm,
#if !BROWSER
            saveAndBackupVm,
#endif
        };

        SelectedCategory = Categories[0];
    }

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        return SelectedCategory is IDismissableEditor editor && editor.DismissActiveEditor();
    }
}
