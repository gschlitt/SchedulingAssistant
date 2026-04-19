using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Represents one semester option in the Add section to which semester? inline prompt
/// shown when the user clicks Add while multiple semesters are loaded.
/// Carries only the semester identity + stored hex color; the view resolves the banner
/// colour via <c>SemesterBackgroundBrushConverter</c> so the ViewModel stays free of
/// Avalonia.Media types.
/// </summary>
public sealed class SemesterPromptItem
{
    /// <summary>Database ID of the semester.</summary>
    public string SemesterId { get; }

    /// <summary>Display name shown on the prompt button, e.g. Fall 2025.</summary>
    public string SemesterName { get; }

    /// <summary>
    /// Stored hex color for the semester (e.g. "#C65D1E"), or empty for name-based fallback.
    /// Consumed by <c>SemesterBackgroundBrushConverter</c> in the prompt button templates.
    /// </summary>
    public string SemesterColor { get; }

    /// <param name="sem">Semester data to represent.</param>
    /// <param name="index">Unused; retained for call-site compatibility.</param>
    public SemesterPromptItem(SemesterDisplay sem, int index)
    {
        SemesterId    = sem.Semester.Id;
        SemesterName  = sem.Semester.Name;
        SemesterColor = sem.Semester.Color ?? string.Empty;
    }
}
