using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 7 (manual path only) — configure section prefixes.
/// Section prefixes are short letter codes prepended to section codes
/// (e.g. prefix "AB" produces sections "AB1", "AB2", "AB3").
/// Each prefix can be linked to a campus set up in the previous step.
/// Section prefixes can be added or changed later in Settings.
/// </summary>
public class Step7SectionPrefixesViewModel : WizardStepViewModel
{
    public override string StepTitle => "Section Prefixes";

    /// <summary>Embeds the section prefix management list.</summary>
    public SectionPrefixListViewModel SectionPrefixes { get; }

    public Step7SectionPrefixesViewModel()
    {
        SectionPrefixes = App.Services.GetRequiredService<SectionPrefixListViewModel>();
    }
}
