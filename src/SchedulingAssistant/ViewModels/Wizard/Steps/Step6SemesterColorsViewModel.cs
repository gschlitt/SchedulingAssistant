using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Editable color row for one semester in the Semester Colors step.
/// Exposes both <see cref="SelectedColor"/> (for the Avalonia color picker)
/// and <see cref="HexColor"/> (used by the orchestrator when writing to the DB).
/// The two properties are kept in sync automatically via their change callbacks.
/// </summary>
public partial class SemesterColorRowViewModel : ViewModelBase
{
    /// <summary>The semester name, used as the row label.</summary>
    public string Name { get; }

    /// <summary>
    /// The current color as an Avalonia <see cref="Color"/> struct.
    /// Bound to the inline <c>ColorView</c>. Changes are reflected in <see cref="HexColor"/>.
    /// </summary>
    [ObservableProperty] private Color _selectedColor;

    /// <summary>
    /// The current color in <c>#RRGGBB</c> hex format.
    /// Written to the DB by the wizard orchestrator. Changes are reflected in <see cref="SelectedColor"/>.
    /// </summary>
    [ObservableProperty] private string _hexColor = string.Empty;


    /// <param name="name">Semester name shown as the row label.</param>
    /// <param name="hexColor">Initial color in <c>#RRGGBB</c> format.</param>
    public SemesterColorRowViewModel(string name, string hexColor)
    {
        Name         = name;
        _selectedColor = ParseHex(hexColor); // initialise backing field directly to avoid double-sync
        HexColor     = hexColor;
    }

    /// <summary>
    /// Keeps <see cref="SelectedColor"/> up to date when <see cref="HexColor"/> is set programmatically
    /// (e.g. when Accept Defaults resets a row).
    /// </summary>
    partial void OnHexColorChanged(string value)
    {
        var parsed = ParseHex(value);
        if (parsed != SelectedColor)
            SelectedColor = parsed;
    }

    /// <summary>
    /// Keeps <see cref="HexColor"/> up to date when the user picks a new colour
    /// via the <c>ColorView</c> flyout.
    /// </summary>
    partial void OnSelectedColorChanged(Color value)
    {
        var hex = ColorToHex(value);
        if (HexColor != hex)
            HexColor = hex;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a hex color string (<c>#RRGGBB</c> or <c>RRGGBB</c>) into an Avalonia <see cref="Color"/>.
    /// Falls back to mid-grey on parse failure.
    /// </summary>
    private static Color ParseHex(string hex) =>
        Color.TryParse(hex, out var c) ? c : Colors.Gray;

    /// <summary>Converts an Avalonia <see cref="Color"/> to an uppercase <c>#RRGGBB</c> string.</summary>
    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>
/// Step 9 (manual path only) — assign display colors to each semester.
/// Colors are pre-populated with position-based defaults; the user can change
/// any of them via the color picker flyout on each row.
/// </summary>
public partial class Step6SemesterColorsViewModel : WizardStepViewModel
{
    /// <summary>
    /// Position-based default semester colors.
    /// Semester 1 receives color index 0, semester 2 receives index 1, and so on,
    /// wrapping if there are more semesters than defaults.
    /// </summary>
    public static readonly string[] DefaultColors =
    [
        "#C65D1E",  // 1 — amber/brown  (Fall)
        "#8F8E8C",  // 2 — grey         (Winter)
        "#7ED957",  // 3 — green        (Spring/Early Summer)
        "#4FB6F7",  // 4 — blue         (Summer)
        "#F2C94C",  // 5 — yellow       (Late Summer)
        "#9B59B6",  // 6 — purple       (fallback)
    ];

    public override string StepTitle => "Semester Colors";

    /// <summary>One row per semester, in the order they were defined in the previous step.</summary>
    public ObservableCollection<SemesterColorRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Populates the color rows from the semester list built in the Academic Year step.
    /// Pre-assigns position-based default colors. Called by the wizard orchestrator.
    /// </summary>
    /// <param name="semesters">Ordered semester definitions from the preceding step.</param>
    public void LoadFromSemesters(IEnumerable<SemesterDefViewModel> semesters)
    {
        Rows.Clear();
        int idx = 0;
        foreach (var sem in semesters)
        {
            var color = !string.IsNullOrWhiteSpace(sem.Color)
                ? sem.Color
                : DefaultColors[idx % DefaultColors.Length];
            Rows.Add(new SemesterColorRowViewModel(sem.Name, color));
            idx++;
        }
    }

    /// <summary>
    /// Resets all rows to the position-based default colors.
    /// </summary>
    [RelayCommand]
    private void AcceptDefaults()
    {
        int idx = 0;
        foreach (var row in Rows)
        {
            row.HexColor = DefaultColors[idx % DefaultColors.Length];
            idx++;
        }
    }
}
