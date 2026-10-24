using System.Globalization;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Small wording helpers shared by the planner's messages, so every message formats times, lengths,
/// lists and day names the same way.
/// </summary>
internal static class ImportText
{
    /// <summary>The full English name of each day number (1 = Monday ... 7 = Sunday); index 0 is unused.</summary>
    private static readonly string[] DayNames =
        ["", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// Where the user finds the scheduling settings (the weekend days and the legal start times), as a
    /// message names it. One constant, so every message that sends the user there says the same thing.
    /// </summary>
    public const string SchedulingSettingsPlace = "Configuration, under Scheduling";

    /// <summary>The most existing names a message lists before saying "and N more".</summary>
    private const int MaxNamesListed = 2;

    /// <summary>Joins items as "A", "A and B" or "A, B and C".</summary>
    /// <param name="items">The items.</param>
    /// <returns>The joined text; empty when there are no items.</returns>
    public static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    /// <summary>The full name of a day number.</summary>
    /// <param name="day">1 = Monday ... 7 = Sunday.</param>
    /// <returns>The English day name.</returns>
    public static string DayName(int day) => DayNames[day];

    /// <summary>
    /// A time in the app's four-digit military form, as the meeting editor shows it
    /// (<c>0730</c>, <c>1045</c>).
    /// </summary>
    /// <param name="minutes">Minutes from midnight.</param>
    /// <returns>The four-digit text.</returns>
    public static string Time(int minutes) => AppDefaults.MinutesToHhmm(minutes);

    /// <summary>
    /// A meeting length as an adjective for a message: whole or half hours are written in hours
    /// ("1-hour", "1.5-hour", "3-hour"); any other length is written in minutes ("50-minute").
    /// </summary>
    /// <param name="minutes">The length in minutes.</param>
    /// <returns>The length text, ready to put before "meetings".</returns>
    public static string LengthAdjective(int minutes) =>
        minutes % 30 == 0
            ? $"{(minutes / 60.0).ToString("0.#", CultureInfo.InvariantCulture)}-hour"
            : $"{minutes.ToString(CultureInfo.InvariantCulture)}-minute";

    /// <summary>Writes "1 meeting" or "30 meetings".</summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The singular noun.</param>
    /// <param name="plural">The plural noun.</param>
    /// <returns>The count and the right noun.</returns>
    public static string Counted(int count, string singular, string plural) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    /// <summary>
    /// Says that some names already exist, naming at most two: "A already exists", "A and B already
    /// exist", "A, B and 3 more already exist".
    /// </summary>
    /// <param name="names">The existing names (at least one).</param>
    /// <returns>The phrase.</returns>
    public static string AlreadyExist(IReadOnlyList<string> names)
    {
        if (names.Count == 1)
            return $"{names[0]} already exists";
        if (names.Count <= MaxNamesListed)
            return $"{JoinWithAnd(names)} already exist";
        var shown = string.Join(", ", names.Take(MaxNamesListed));
        return $"{shown} and {names.Count - MaxNamesListed} more already exist";
    }
}
