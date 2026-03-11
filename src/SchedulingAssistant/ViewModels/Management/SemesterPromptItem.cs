using Avalonia;
using Avalonia.Media;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Represents one semester option in the Add section to which semester? inline prompt
/// shown when the user clicks Add while multiple semesters are loaded.
/// Carries the semester identity and the same banner color as its corresponding
/// SemesterBannerViewModel so the prompt feels visually consistent.
/// </summary>
public sealed class SemesterPromptItem
{
    /// <summary>Database ID of the semester.</summary>
    public string SemesterId { get; }

    /// <summary>Display name shown on the prompt button, e.g. Fall 2025.</summary>
    public string SemesterName { get; }

    /// <summary>Background brush for the prompt button, matching the semester banner color.</summary>
    public IBrush? BannerBackground { get; }

    /// <param name=sem>Semester data to represent.</param>
    /// <param name=index>Zero-based position in the selected semester list (determines color slot).</param>
    public SemesterPromptItem(SemesterDisplay sem, int index)
    {
        SemesterId   = sem.Semester.Id;
        SemesterName = sem.Semester.Name;

        // Color is assigned by semester name, not position, so it matches the banner in the list.
        string bgKey = GetBannerColorKey(sem.Semester.Name);
        object? bg = null;
        Application.Current?.Resources.TryGetResource(bgKey, null, out bg);
        BannerBackground = bg as IBrush;
    }

    /// <summary>
    /// Maps a semester name (e.g., "Fall 2025") to its corresponding AppColors background brush key.
    /// Names are matched by their first word to remain consistent across all selected semesters.
    /// </summary>
    private static string GetBannerColorKey(string semesterName)
    {
        var firstWord = semesterName.Split(' ')[0];
        return firstWord switch
        {
            "Fall" => "FallBackground",
            "Winter" => "WinterBackground",
            "Early" => "EarlySummerBackground",  // "Early Summer" starts with "Early"
            "Summer" => "SummerBackground",
            "Late" => "LateSummerBackground",
            _ => "FallBackground"  // fallback
        };
    }
}
