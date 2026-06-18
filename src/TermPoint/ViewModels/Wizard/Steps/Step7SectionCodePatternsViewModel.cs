using TermPoint.ViewModels.Management;

namespace TermPoint.ViewModels.Wizard.Steps;

/// <summary>
/// Step 9 (manual path only) — configure section code patterns.
/// Section code patterns define how section codes are generated (e.g. "A, B, C" or "01, 02, 03").
/// Each pattern specifies a prefix, an incrementing part (numeric or alphabetic), and an optional
/// suffix. Patterns can be changed later in Settings.
/// </summary>
public class Step7SectionCodePatternsViewModel : WizardStepViewModel
{
    public override string StepTitle => "Section Codes";

    /// <summary>Embeds the section code pattern management list.</summary>
    public SectionCodePatternListViewModel SectionCodePatterns { get; }

    /// <param name="sectionCodePatterns">
    /// The section code pattern management VM to embed. Injected by the wizard orchestrator so
    /// tests can supply a version backed by a real or in-memory database without
    /// requiring the live DI container.
    /// </param>
    public Step7SectionCodePatternsViewModel(SectionCodePatternListViewModel sectionCodePatterns)
    {
        SectionCodePatterns = sectionCodePatterns;
    }
}
