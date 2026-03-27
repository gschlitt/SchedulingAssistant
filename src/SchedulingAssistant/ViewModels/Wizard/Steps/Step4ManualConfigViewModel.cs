using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 4 (manual path only) — set up campuses.
/// Campuses are physical locations that can be associated with rooms and section prefixes.
/// This step is optional; campuses can be added or changed later in Settings.
/// </summary>
public class Step4CampusesViewModel : WizardStepViewModel
{
    public override string StepTitle => "Campuses";

    /// <summary>Embeds the campus management list.</summary>
    public CampusListViewModel Campuses { get; }

    /// <param name="campuses">
    /// The campus management VM to embed. Injected by the wizard orchestrator so
    /// tests can supply a version backed by a real or in-memory database without
    /// requiring the live DI container.
    /// </param>
    public Step4CampusesViewModel(CampusListViewModel campuses)
    {
        Campuses = campuses;
    }
}
