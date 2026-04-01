using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>Editable row for a single semester in the step 5 semester list.</summary>
public partial class SemesterDefViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Hex color string assigned in step 9. Empty until step 9 fills it in.</summary>
    public string Color { get; set; } = string.Empty;
}

/// <summary>
/// Step 8 — enter the first academic year name and build the semester list.
/// On the import path, semesters are pre-populated from the .tpconfig; the user may still edit them.
/// On the manual path, semesters are pre-seeded with Fall, Winter, and Spring as a starting point.
/// </summary>
public partial class Step5AcademicYearViewModel : WizardStepViewModel
{
    public override string StepTitle => "First Academic Year";

    // Matches a bare 4-digit year such as "2024".
    private static readonly Regex YearPattern = new(@"^\d{4}$", RegexOptions.Compiled);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(AcademicYearNameError))]
    private string _academicYearName = string.Empty;

    /// <summary>
    /// Validation message shown below the academic year name field.
    /// Null when the field is empty (no error yet) or when the value is acceptable.
    /// </summary>
    public string? AcademicYearNameError
    {
        get
        {
            var trimmed = AcademicYearName.Trim();
            if (trimmed.Length == 0)
                return null; // no message while the field is still empty

            // Accept a bare 4-digit year (will be expanded on commit) or the full "YYYY-YYYY+1" form.
            if (!YearPattern.IsMatch(trimmed) && !(trimmed.Length == 9 && trimmed[4] == '-'))
                return "Enter the start year (e.g. 2025) and it will expand automatically.";

            if (trimmed.Length == 9)
            {
                // Validate the YYYY-YYYY+1 structure.
                if (!int.TryParse(trimmed[..4], out var y1) || !int.TryParse(trimmed[5..], out var y2))
                    return "Use the format YYYY-YYYY (e.g. 2025-2026).";
                if (y2 != y1 + 1)
                    return $"The second year must be {y1 + 1}.";
            }

            return null;
        }
    }

    /// <summary>
    /// Expands a bare 4-digit start year to the canonical "YYYY-YYYY+1" form.
    /// Leaves the value unchanged if it is already in the full form or is unrecognised.
    /// </summary>
    /// <param name="raw">The raw text entered by the user.</param>
    /// <returns>The expanded or unchanged name, trimmed of whitespace.</returns>
    private static string ExpandName(string raw)
    {
        var trimmed = raw.Trim();
        if (YearPattern.IsMatch(trimmed))
        {
            var y1 = int.Parse(trimmed);
            return $"{y1}-{y1 + 1}";
        }
        return trimmed;
    }

    /// <summary>
    /// Called when the academic year name TextBox loses focus.
    /// Expands a bare 4-digit year entry (e.g. "2025" → "2025-2026") in place.
    /// </summary>
    [RelayCommand]
    private void CommitAcademicYearName() => AcademicYearName = ExpandName(AcademicYearName);

    /// <summary>
    /// The expanded, canonical academic year name ready for DB insertion.
    /// Callers should use this rather than <see cref="AcademicYearName"/> directly.
    /// </summary>
    public string ExpandedAcademicYearName => ExpandName(AcademicYearName);

    /// <summary>
    /// Requires a non-empty, valid academic year name and at least one semester.
    /// </summary>
    public override bool CanAdvance =>
        !string.IsNullOrWhiteSpace(AcademicYearName)
        && AcademicYearNameError is null
        && Semesters.Count > 0;

    /// <summary>
    /// Semesters in this academic year, in display order.
    /// Pre-seeded from <see cref="AppDefaults.Semesters"/> on the manual path;
    /// replaced by <see cref="LoadFromConfig"/> on the import path.
    /// </summary>
    public ObservableCollection<SemesterDefViewModel> Semesters { get; } =
        new(AppDefaults.Semesters.Select(s => new SemesterDefViewModel { Name = s.Name }));

    /// <summary>
    /// Pre-populates the semester list from imported .tpconfig data.
    /// Called by the wizard orchestrator when the import path is taken.
    /// </summary>
    /// <param name="defs">Semester definitions from the imported .tpconfig file.</param>
    public void LoadFromConfig(IEnumerable<TpConfigSemesterDef> defs)
    {
        Semesters.Clear();
        foreach (var def in defs)
            Semesters.Add(new SemesterDefViewModel { Name = def.Name, Color = def.Color });

        OnPropertyChanged(nameof(CanAdvance));
    }

    [RelayCommand]
    private void AddSemester()
    {
        Semesters.Add(new SemesterDefViewModel());
        OnPropertyChanged(nameof(CanAdvance));
    }

    [RelayCommand]
    private void RemoveSemester(SemesterDefViewModel vm)
    {
        Semesters.Remove(vm);
        OnPropertyChanged(nameof(CanAdvance));
    }
}
