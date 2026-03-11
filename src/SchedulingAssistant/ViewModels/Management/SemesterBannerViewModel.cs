using Avalonia;
using Avalonia.Media;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Represents a semester group header row in the Section List.
/// Carries the semester identity and display colors resolved from
/// application resources (SemesterBanner1-5Background/Border).
/// Colors cycle through 5 named slots by the semester position index.
/// This is a plain (non-observable) ViewModel: banners are rebuilt from scratch
/// on each load, so no change notification is needed.
/// </summary>
public class SemesterBannerViewModel : ISectionListEntry
{
    /// <summary>Database ID of the semester this banner heads.</summary>
    public string SemesterId { get; }

    /// <summary>Display name shown in the banner, e.g. Fall 2025.</summary>
    public string SemesterName { get; }

    /// <summary>Background fill for the banner row, resolved from AppColors.</summary>
    public IBrush? BannerBackground { get; }

    /// <summary>Bottom-border color for the banner row, resolved from AppColors.</summary>
    public IBrush? BannerBorder { get; }

    /// <summary>
    /// Constructs a semester banner ViewModel.
    /// </summary>
    /// <param name=sem>The semester data to represent.</param>
    /// <param name=index>
    /// Zero-based position of this semester in the selected list.
    /// Determines which color slot (1-5) to use from AppColors.
    /// </param>
    public SemesterBannerViewModel(SemesterDisplay sem, int index)
    {
        SemesterId   = sem.Semester.Id;
        SemesterName = sem.Semester.Name;

        // Colors are assigned by semester name (e.g., Fall, Winter, Summer),
        // not by position, so they remain consistent regardless of which semesters are selected.
        string bgKey = GetBannerColorKey(sem.Semester.Name);
        string bdKey = GetBannerBorderKey(sem.Semester.Name);

        object? bg = null, bd = null;
        Application.Current?.Resources.TryGetResource(bgKey, null, out bg);
        Application.Current?.Resources.TryGetResource(bdKey, null, out bd);

        BannerBackground = bg as IBrush;
        BannerBorder     = bd as IBrush;
    }

    /// <summary>
    /// Maps a semester name (e.g., "Fall 2025", "Winter 2026") to its corresponding
    /// AppColors background brush key. Names are matched by their first word.
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

    /// <summary>
    /// Maps a semester name to its corresponding AppColors border brush key.
    /// </summary>
    private static string GetBannerBorderKey(string semesterName)
    {
        var firstWord = semesterName.Split(' ')[0];
        return firstWord switch
        {
            "Fall" => "FallBorder",
            "Winter" => "WinterBorder",
            "Early" => "EarlySummerBorder",
            "Summer" => "SummerBorder",
            "Late" => "LateSummerBorder",
            _ => "FallBorder"
        };
    }
}
