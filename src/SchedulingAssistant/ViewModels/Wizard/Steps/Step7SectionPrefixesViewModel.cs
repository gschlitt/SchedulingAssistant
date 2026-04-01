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
    public override string StepTitle => "Section Prefixes and Designators";

    /// <summary>Embeds the section prefix management list.</summary>
    public SectionPrefixListViewModel SectionPrefixes { get; }

    /// <param name="sectionPrefixes">
    /// The section prefix management VM to embed. Injected by the wizard orchestrator so
    /// tests can supply a version backed by a real or in-memory database without
    /// requiring the live DI container. This VM must be constructed with the same
    /// <see cref="ICampusRepository"/> that backs the Campuses step (step 4) so that
    /// campuses added during that step appear in the prefix campus dropdown.
    /// </param>
    public Step7SectionPrefixesViewModel(SectionPrefixListViewModel sectionPrefixes)
    {
        SectionPrefixes = sectionPrefixes;
    }
}
