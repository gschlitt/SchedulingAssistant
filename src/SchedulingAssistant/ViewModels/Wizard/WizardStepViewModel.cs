using SchedulingAssistant.ViewModels;

namespace SchedulingAssistant.ViewModels.Wizard;

/// <summary>
/// Abstract base for all startup wizard step ViewModels.
/// Each step declares its title and whether the user may advance past it.
/// </summary>
public abstract class WizardStepViewModel : ViewModelBase
{
    /// <summary>Display title shown in the wizard header for this step.</summary>
    public abstract string StepTitle { get; }

    /// <summary>
    /// True when all required inputs for this step are valid and the user may click Next/Finish.
    /// Override in steps that perform validation.
    /// </summary>
    public virtual bool CanAdvance => true;

    /// <summary>
    /// Inline error message shown beneath the step's form (empty string = no error).
    /// Steps set this when validation fails so the user can see what to fix without modals.
    /// </summary>
    public virtual string ErrorMessage => string.Empty;
}
