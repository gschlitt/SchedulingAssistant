using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 6 (manual path only) — configure block patterns.
/// Block patterns map each block length to its typical meeting-day combinations
/// (e.g. a 1.5h block might meet MWF or TR). TermPoint uses these to suggest
/// day combinations when you add a section. Patterns can be changed later in Settings.
/// </summary>
public class Step6BlockPatternsViewModel : WizardStepViewModel
{
    public override string StepTitle => "Block Patterns";

    /// <summary>Embeds the block pattern management list.</summary>
    public BlockPatternListViewModel BlockPatterns { get; }

    /// <param name="blockPatterns">
    /// The block pattern management VM to embed. Injected by the wizard orchestrator so
    /// tests can supply a version backed by a real or in-memory database without
    /// requiring the live DI container.
    /// </param>
    public Step6BlockPatternsViewModel(BlockPatternListViewModel blockPatterns)
    {
        BlockPatterns = blockPatterns;
    }
}
