using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 1 — collect institution name/abbreviation and academic unit name/abbreviation.
/// All four fields are required before advancing.
/// </summary>
public partial class Step1InstitutionViewModel : WizardStepViewModel
{
    public override string StepTitle => "Institution & Academic Unit";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _institutionName = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _institutionAbbrev = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _acUnitName = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _acUnitAbbrev = string.Empty;

    public override bool CanAdvance =>
        !string.IsNullOrWhiteSpace(InstitutionName) &&
        !string.IsNullOrWhiteSpace(InstitutionAbbrev) &&
        !string.IsNullOrWhiteSpace(AcUnitName) &&
        !string.IsNullOrWhiteSpace(AcUnitAbbrev);
}
