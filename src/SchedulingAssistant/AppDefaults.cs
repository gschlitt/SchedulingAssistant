namespace SchedulingAssistant;

/// <summary>
/// Application-wide default values used to pre-populate the startup wizard.
/// These are example values the user can edit freely; they represent nothing
/// institution-specific. They are the single authoritative source for all
/// seeded "example" data — no other file should hardcode semester names,
/// colors, block lengths, or start times.
///
/// Data flows:
///   Wizard (manual path) → wizard steps read from here → user edits → written to DB
///   Wizard (import path) → .tpconfig overrides these; these are never used
///   Files / New Database (transfer) → transferred config used; these are never used
///   Files / New Database (no transfer) → nothing seeded; these are never used
/// </summary>
public static class AppDefaults
{
    /// <summary>
    /// Default semesters, in display order.
    /// Each entry carries both the name shown to the user and the initial hex color
    /// assigned on the Semester Colors wizard step. Colors are position-based; if the
    /// user adds more semesters than there are entries here the extras get no preset color.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string HexColor)> Semesters =
    [
        ("Fall",   "#C65D1E"),   // amber / brown
        ("Winter", "#8F8E8C"),   // grey
        ("Spring", "#7ED957"),   // green
    ];

    /// <summary>
    /// Default block lengths and their legal start times, in minutes from midnight.
    /// Displayed in the Block Lengths &amp; Start Times wizard step as an editable starting
    /// point. The user may add, remove, or modify any entry before finishing the wizard.
    ///
    /// Converting minutes to HHMM: <c>h = m / 60; min = m % 60; $"{h:D2}{min:D2}"</c>
    /// </summary>
    public static readonly IReadOnlyList<(double BlockHours, int[] StartMinutes)> LegalStartTimes =
    [
        // 1.5-hour blocks: 08:30–18:00
        (1.5, [510, 600, 690, 780, 870, 960, 1050, 1080]),
        // 2-hour blocks: 08:30–20:00
        (2.0, [510, 540, 630, 780, 900, 1050, 1080, 1170, 1200]),
        // 3-hour blocks: 08:30–19:00
        (3.0, [510, 690, 870, 960, 1050, 1080, 1110, 1140]),
        // 4-hour blocks: 08:30, 13:00, 17:30, 18:00
        (4.0, [510, 780, 1050, 1080]),
    ];

    /// <summary>
    /// Converts a start time in minutes from midnight to 4-digit military HHMM format
    /// (e.g. 510 → "0830", 780 → "1300"). Used when pre-populating wizard text fields.
    /// </summary>
    /// <param name="minutes">Minutes from midnight (0–1439).</param>
    /// <returns>4-character HHMM string.</returns>
    public static string MinutesToHhmm(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}
