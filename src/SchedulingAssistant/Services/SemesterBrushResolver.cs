using Avalonia;
using Avalonia.Media;

namespace SchedulingAssistant.Services;

/// <summary>
/// View-layer helper that resolves a semester's display <see cref="IBrush"/> from its stored
/// hex color or (fallback) its name-based AppColors resource key. Consolidates the brush
/// construction logic that previously lived in <c>ScheduleGridViewModel</c> so ViewModels
/// no longer reference <c>Avalonia.Media</c> types.
/// </summary>
public static class SemesterBrushResolver
{
    /// <summary>
    /// Resolves a single brush used for a semester's border (and, when no hex color is stored,
    /// its background if the caller uses the same brush for both).
    /// Priority: explicit hex first, then name-based AppColors lookup.
    /// Returns null if both semesterName and hexColor are empty/null, or if neither source yields a brush.
    /// </summary>
    /// <param name="semesterName">Semester name (used as fallback key — first word matched).</param>
    /// <param name="hexColor">Optional hex color string stored on the Semester model.</param>
    public static IBrush? Resolve(string semesterName, string hexColor = "")
    {
        if (!string.IsNullOrWhiteSpace(hexColor))
        {
            try { return new SolidColorBrush(Color.Parse(hexColor)); }
            catch { /* fall through to name-based lookup */ }
        }

        // If both are empty, return null (uncolored).
        if (string.IsNullOrEmpty(semesterName) && string.IsNullOrEmpty(hexColor))
            return null;

        var key = BorderKey(semesterName);
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
            return resource as IBrush;

        return null;
    }

    /// <summary>
    /// Resolves the (background, border) brush pair for a semester.
    /// When a hex color is stored, the same brush is used for both.
    /// Otherwise each is looked up separately in AppColors by semester name.
    /// Returns (null, null) if both semesterName and hexColor are empty/null.
    /// </summary>
    /// <param name="semesterName">Semester name (used as fallback key — first word matched).</param>
    /// <param name="hexColor">Optional hex color string stored on the Semester model.</param>
    public static (IBrush? bg, IBrush? bd) ResolvePair(string semesterName, string hexColor = "")
    {
        if (!string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var brush = new SolidColorBrush(Color.Parse(hexColor));
                return (brush, brush);
            }
            catch { /* fall through */ }
        }

        // If both are empty, return no brushes (uncolored).
        if (string.IsNullOrEmpty(semesterName) && string.IsNullOrEmpty(hexColor))
            return (null, null);

        var firstWord = FirstWord(semesterName);
        var (bgKey, bdKey) = firstWord switch
        {
            "Fall"   => ("FallBackground",        "FallBorder"),
            "Winter" => ("WinterBackground",      "WinterBorder"),
            "Early"  => ("EarlySummerBackground", "EarlySummerBorder"),
            "Summer" => ("SummerBackground",      "SummerBorder"),
            "Late"   => ("LateSummerBackground",  "LateSummerBorder"),
            _        => ("FallBackground",        "FallBorder")
        };

        var app = Application.Current;
        IBrush? bg = null, bd = null;
        if (app != null)
        {
            if (app.Resources.TryGetResource(bgKey, null, out var bgObj)) bg = bgObj as IBrush;
            if (app.Resources.TryGetResource(bdKey, null, out var bdObj)) bd = bdObj as IBrush;
        }
        return (bg, bd);
    }

    private static string BorderKey(string semesterName) => FirstWord(semesterName) switch
    {
        "Fall"   => "FallBorder",
        "Winter" => "WinterBorder",
        "Early"  => "EarlySummerBorder",
        "Summer" => "SummerBorder",
        "Late"   => "LateSummerBorder",
        _        => "FallBorder"
    };

    private static string FirstWord(string s) => s.Split(' ')[0];
}
