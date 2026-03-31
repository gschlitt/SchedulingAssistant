using Avalonia;
using Avalonia.Media;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;

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

        // Use the semester's assigned hex color if available, otherwise fall back to name-based lookup
        string semesterColor = sem.Semester.Color ?? string.Empty;

        // For background, we'll use a color derived from the border (same hex color)
        IBrush? bgBrush = ScheduleGridViewModel.ResolveSemesterBorderBrush(sem.Semester.Name, semesterColor);
        if (bgBrush is SolidColorBrush solidBrush)
        {
            // Convert the border color to a lighter version for the background
            var color = solidBrush.Color;
            var lighterColor = Color.FromArgb(
                (byte)((color.A + 255) / 2),
                (byte)((color.R + 255) / 2),
                (byte)((color.G + 255) / 2),
                (byte)((color.B + 255) / 2));
            BannerBackground = new SolidColorBrush(lighterColor);
        }
        else
        {
            // Fallback to name-based lookup for background
            string bgKey = GetBannerColorKey(sem.Semester.Name);
            object? bg = null;
            Application.Current?.Resources.TryGetResource(bgKey, null, out bg);
            BannerBackground = bg as IBrush;
        }

        BannerBorder = bgBrush;
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
