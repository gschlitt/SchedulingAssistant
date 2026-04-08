namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Wizard step 1 — License Agreement.
/// Read-only; no user input required. The user clicks Next to accept and continue.
/// </summary>
public class StepLicenseViewModel : WizardStepViewModel
{
    public override string StepTitle => "License Agreement";
}
