using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Represents a semester group header row in the Section List and Meeting List.
/// Carries only the semester identity + stored hex color; the view resolves the
/// actual brush via <c>SemesterBorderBrushConverter</c>, keeping Avalonia.Media
/// types out of the ViewModel layer.
/// This is a plain (non-observable) ViewModel: banners are rebuilt from scratch
/// on each load, so no change notification is needed.
/// </summary>
public class SemesterBannerViewModel : ISectionListEntry, IMeetingListEntry
{
    /// <summary>Database ID of the semester this banner heads.</summary>
    public string SemesterId { get; }

    /// <summary>Display name shown in the banner, e.g. Fall 2025.</summary>
    public string SemesterName { get; }

    /// <summary>
    /// Stored hex color for the semester (e.g. "#C65D1E"), or empty for name-based fallback.
    /// Consumed by <c>SemesterBorderBrushConverter</c> in the banner XAML templates.
    /// </summary>
    public string SemesterColor { get; }

    /// <summary>
    /// Constructs a semester banner ViewModel.
    /// </summary>
    /// <param name="sem">The semester data to represent.</param>
    /// <param name="index">Unused; retained for call-site compatibility.</param>
    public SemesterBannerViewModel(SemesterDisplay sem, int index)
    {
        SemesterId    = sem.Semester.Id;
        SemesterName  = sem.Semester.Name;
        SemesterColor = sem.Semester.Color ?? string.Empty;
    }
}
