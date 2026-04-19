using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Editable color row for one semester in the Semester Colors step.
/// Exposes only <see cref="HexColor"/> (a <c>#RRGGBB</c> string); the view binds this to
/// <c>ColorView.Color</c> via <c>HexToColorConverter</c>, which keeps all Avalonia.Media
/// types out of the ViewModel layer.
/// </summary>
public partial class SemesterColorRowViewModel : ViewModelBase
{
    /// <summary>The semester name, used as the row label.</summary>
    public string Name { get; }

    /// <summary>
    /// The current color in <c>#RRGGBB</c> hex format. Bound two-way (via the hex-to-color
    /// converter) to the inline <c>ColorView</c>, and read by the wizard orchestrator when
    /// writing to the DB.
    /// </summary>
    [ObservableProperty] private string _hexColor = string.Empty;

    /// <param name="name">Semester name shown as the row label.</param>
    /// <param name="hexColor">Initial color in <c>#RRGGBB</c> format.</param>
    public SemesterColorRowViewModel(string name, string hexColor)
    {
        Name      = name;
        _hexColor = hexColor;
    }
}

/// <summary>
/// Step 9 (manual path only) — assign display colors to each semester.
/// Colors are pre-populated from <see cref="AppDefaults.Semesters"/> by position;
/// the user can change any of them via the color picker flyout on each row.
/// </summary>
public partial class Step6SemesterColorsViewModel : WizardStepViewModel
{
    public override string StepTitle => "Semester Colors";

    /// <summary>One row per semester, in the order they were defined in the previous step.</summary>
    public ObservableCollection<SemesterColorRowViewModel> Rows { get; } = [];

    /// <summary>
    /// The default hex colors, by position, sourced from <see cref="AppDefaults.Semesters"/>.
    /// Used by <see cref="AcceptDefaultsCommand"/> and by tests to verify position-based assignment.
    /// </summary>
    public static IReadOnlyList<string> DefaultColors =>
        AppDefaults.Semesters.Select(s => s.HexColor).ToList();

    /// <summary>
    /// Resets every row's color to the position-based default from <see cref="DefaultColors"/>,
    /// clamped to the last entry for any rows beyond the defaults list.
    /// </summary>
    [RelayCommand]
    private void AcceptDefaults()
    {
        var defaults = DefaultColors;
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].HexColor = defaults[Math.Min(i, defaults.Count - 1)];
    }

    /// <summary>
    /// Populates the color rows from the semester list built in the Academic Year step.
    /// Pre-assigns colors from <see cref="AppDefaults.Semesters"/> by position; falls back
    /// to the last AppDefaults entry's color for any semesters beyond the defaults list.
    /// Called by the wizard orchestrator.
    /// </summary>
    /// <param name="semesters">Ordered semester definitions from the preceding step.</param>
    public void LoadFromSemesters(IEnumerable<SemesterDefViewModel> semesters)
    {
        Rows.Clear();
        int idx = 0;
        foreach (var sem in semesters)
        {
            // Use a color already on the SemesterDef (e.g. from a .tpconfig import),
            // then fall back to AppDefaults by position, clamped to the last entry.
            string color;
            if (!string.IsNullOrWhiteSpace(sem.Color))
                color = sem.Color;
            else
            {
                var defaults = AppDefaults.Semesters;
                color = defaults[Math.Min(idx, defaults.Count - 1)].HexColor;
            }
            Rows.Add(new SemesterColorRowViewModel(sem.Name, color));
            idx++;
        }
    }
}
