using System.Text.RegularExpressions;

namespace SchedulingAssistant.Services;

/// <summary>
/// Parses a course number string (e.g. "101", "111LAB", "LAB111") to derive a
/// course level — the hundreds band expressed as an integer string ("0", "100",
/// "200", … "900").
///
/// The detection rule: the course number must consist of an optional non-digit
/// prefix, exactly three consecutive decimal digits, and an optional non-digit
/// suffix.  If that pattern is satisfied, the level equals (those three digits /
/// 100) × 100 (integer division), expressed as a string.  Otherwise the method
/// returns <see langword="null"/>.
///
/// Examples:
///   "101"    → "100"
///   "348"    → "300"
///   "111LAB" → "100"
///   "LAB111" → "100"
///   "999"    → "900"
///   "001"    → "0"
///   "1111"   → null  (four consecutive digits — no clean three-digit block)
///   "AB"     → null  (no digits at all)
///   ""       → null
/// </summary>
public static class CourseLevelParser
{
    // Matches: optional non-digit prefix, exactly 3 digits, optional non-digit suffix.
    // The full string must be consumed (^ and $), so "1111" does NOT match because
    // after the greedy \d{3} eats "111" the remaining "1" fails [A-Za-z]*.
    private static readonly Regex _pattern =
        new(@"^[A-Za-z]*(\d{3})[A-Za-z]*$", RegexOptions.Compiled);

    /// <summary>
    /// Attempts to extract a course level from a course number string.
    /// </summary>
    /// <param name="courseNumber">
    /// The raw course number exactly as the user typed it (e.g. "101", "111LAB").
    /// Leading and trailing whitespace is trimmed before matching.
    /// </param>
    /// <returns>
    /// The level string ("0", "100", "200", … "900") when the pattern is matched;
    /// <see langword="null"/> otherwise.
    /// </returns>
    public static string? ParseLevel(string? courseNumber)
    {
        if (string.IsNullOrWhiteSpace(courseNumber))
            return null;

        var match = _pattern.Match(courseNumber.Trim());
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var num))
            return null;

        return ((num / 100) * 100).ToString();
    }

    /// <summary>
    /// The fixed, ordered list of all valid level strings shown in the Level
    /// combo-box and schedule-grid filter.  "0" represents courses numbered 000–099,
    /// "100" represents 100–199, and so on through "900" for 900–999.
    /// </summary>
    public static IReadOnlyList<string> AllLevels { get; } =
        new[] { "0", "100", "200", "300", "400", "500", "600", "700", "800", "900" };
}
