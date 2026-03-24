namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 10 — closing/congratulations panel shown after all configuration is complete.
/// Clicking Finish here is the point at which IsInitialSetupComplete is set to true.
/// No validation is required; the step is always ready to advance.
/// </summary>
public class Step10ClosingViewModel : WizardStepViewModel
{
    public override string StepTitle => "You're All Set";
    public override bool CanAdvance  => true;
}
