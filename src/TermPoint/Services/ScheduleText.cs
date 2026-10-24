using System.Text.RegularExpressions;

namespace TermPoint.Services;

/// <summary>
/// The outcome of parsing a piece of text typed (or pasted) by a person: either a value or a
/// plain-English message saying what is wrong with the text.
/// </summary>
/// <typeparam name="T">The type of the parsed value.</typeparam>
/// <param name="Value">
/// The parsed value. Meaningful only when <see cref="IsValid"/> is true; <c>default</c> otherwise.
/// A valid result may still carry a null value where null is itself meaningful (for example a
/// weekly frequency), so test <see cref="IsValid"/>, never the value, to detect success.
/// </param>
/// <param name="Error">
/// A message ready to show a person, with no row or column prefix (the caller adds that), or null
/// when the text parsed.
/// </param>
public sealed record ParseResult<T>(T Value, string? Error)
{
    /// <summary>True when the text parsed and <see cref="Value"/> can be used.</summary>
    public bool IsValid => Error is null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The parsed value.</param>
    public static ParseResult<T> Ok(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The plain-English reason the text could not be parsed.</param>
    public static ParseResult<T> Fail(string error) => new(default!, error);
}

/// <summary>
/// Parses meeting text the way people actually type it: day lists ("MWF", "Tue, Thu"), times
/// ("1000", "10:00", "10 AM"), week lists ("1, 6,7") and frequencies ("odd weeks"). Every method
/// returns a <see cref="ParseResult{T}"/> holding either the value or a plain-English message;
/// none throws on bad input. Pure static code with no UI dependency, shared by the section importer
/// and (for week lists) the meeting editor so both apply identical rules and wording.
/// <para>
/// None of these methods applies the scheduling limits (07:30 to 22:00); that is the caller's job
/// (see <see cref="MeetingTimeLimits"/>).
/// </para>
/// </summary>
public static class ScheduleText
{
    // ── Days ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every accepted day name, case-insensitive, mapped to its day number (1 = Monday ... 7 = Sunday).
    /// "S" is deliberately absent: it is ambiguous between Saturday and Sunday.
    /// </summary>
    private static readonly Dictionary<string, int> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Monday"] = 1,    ["Mon"] = 1, ["Mo"] = 1, ["M"] = 1,
        ["Tuesday"] = 2,   ["Tue"] = 2, ["Tu"] = 2, ["T"] = 2,
        ["Wednesday"] = 3, ["Wed"] = 3, ["We"] = 3, ["W"] = 3,
        ["Thursday"] = 4,  ["Thu"] = 4, ["Th"] = 4, ["R"] = 4,
        ["Friday"] = 5,    ["Fri"] = 5, ["Fr"] = 5, ["F"] = 5,
        ["Saturday"] = 6,  ["Sat"] = 6, ["Sa"] = 6,
        ["Sunday"] = 7,    ["Sun"] = 7, ["Su"] = 7,
    };

    /// <summary>The two-letter day names tried first when splitting a compact run such as "MTWThF".</summary>
    private static readonly string[] TwoLetterDays = ["Th", "Tu", "Sa", "Su", "Mo", "We", "Fr"];

    /// <summary>The one-letter day names tried second when splitting a compact run.</summary>
    private static readonly string[] OneLetterDays = ["M", "T", "W", "R", "F"];

    /// <summary>The full English name of each day number, for messages. Index 0 is unused.</summary>
    private static readonly string[] FullDayNames =
        ["", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// Parses a list of meeting days.
    /// <para>
    /// Tokens are separated by commas and/or whitespace. A token is a day name (see
    /// <see cref="DayNames"/>: Monday/Mon/Mo/M, Tuesday/Tue/Tu/T, Wednesday/Wed/We/W,
    /// Thursday/Thu/Th/R, Friday/Fri/Fr/F, Saturday/Sat/Sa, Sunday/Sun/Su, any case) or a compact run
    /// of letters with no separators ("MWF", "TR", "TTh", "MTWThF"). A compact run is split greedily
    /// left to right, trying the two-letter names before the one-letter names; if any part cannot be
    /// matched, the whole token is an error. "S" alone is unknown because it could mean either weekend day.
    /// </para>
    /// </summary>
    /// <param name="text">The text as typed, e.g. "M W F", "Tue, Thu" or "MWF". May be null.</param>
    /// <returns>
    /// The day numbers (1 = Monday ... 7 = Sunday) in the order given, or an error when the text is
    /// blank, contains something that is not a day, or lists a day twice.
    /// </returns>
    public static ParseResult<IReadOnlyList<int>> ParseDays(string? text)
    {
        var tokens = (text ?? "").Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return ParseResult<IReadOnlyList<int>>.Fail("Enter the meeting days, e.g. M W F or Tuesday, Thursday.");

        var days = new List<int>();
        foreach (var token in tokens)
        {
            var tokenDays = SplitDayToken(token);
            if (tokenDays is null)
                return ParseResult<IReadOnlyList<int>>.Fail(
                    $"\"{token}\" isn't a day. Use M, T, W, R (or Th), F, Sa, Su, or the full day names.");

            foreach (var day in tokenDays)
            {
                if (days.Contains(day))
                    return ParseResult<IReadOnlyList<int>>.Fail($"{FullDayNames[day]} is listed twice.");
                days.Add(day);
            }
        }

        return ParseResult<IReadOnlyList<int>>.Ok(days);
    }

    /// <summary>
    /// Resolves one separator-free token to day numbers: first as a whole day name, then (when the
    /// token is only letters) as a compact run split greedily left to right.
    /// </summary>
    /// <param name="token">A non-empty token with no commas or whitespace.</param>
    /// <returns>The day numbers the token stands for, or null when any part is not a day.</returns>
    private static List<int>? SplitDayToken(string token)
    {
        if (DayNames.TryGetValue(token, out var single))
            return [single];

        if (!token.All(char.IsAsciiLetter))
            return null;

        var result = new List<int>();
        int pos = 0;
        while (pos < token.Length)
        {
            string? match = FindDayAt(token, pos, TwoLetterDays) ?? FindDayAt(token, pos, OneLetterDays);
            if (match is null)
                return null;

            result.Add(DayNames[match]);
            pos += match.Length;
        }
        return result;
    }

    /// <summary>Returns the first name in <paramref name="candidates"/> that <paramref name="token"/> has at <paramref name="pos"/>, ignoring case.</summary>
    /// <param name="token">The token being split.</param>
    /// <param name="pos">The index at which to look.</param>
    /// <param name="candidates">The day names to try, in priority order.</param>
    /// <returns>The matching candidate, or null when none matches.</returns>
    private static string? FindDayAt(string token, int pos, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.Compare(token, pos, candidate, 0, candidate.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return candidate;
        }
        return null;
    }

    // ── Times ─────────────────────────────────────────────────────────────────

    /// <summary>Wording shared by the time error messages: the forms a person may type.</summary>
    private const string TimeFormsHint = "a time like 1000, 10:00 or 10:00 AM";

    /// <summary>3 or 4 digits, "HMM" or "HHMM" (930, 0930, 1345).</summary>
    private static readonly Regex CompactTime = new(@"^([0-9]{1,2})([0-9]{2})$", RegexOptions.CultureInvariant);

    /// <summary>24-hour "H:MM" or "HH:MM", optionally with ":00" seconds (9:30, 13:45, 10:00:00).</summary>
    private static readonly Regex ColonTime = new(@"^([0-9]{1,2}):([0-9]{2})(?::00)?$", RegexOptions.CultureInvariant);

    /// <summary>12-hour time with AM/PM, spacing and minutes optional ("10 AM", "1:30pm", "10:00 AM").</summary>
    private static readonly Regex MeridiemTime = new(
        @"^([0-9]{1,2})(?::([0-9]{2}))?\s*([ap]m)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses a time of day. Accepts 3 or 4 digits ("930", "0930", "1000", "1345"), 24-hour with a
    /// colon ("10:00", "9:30", "13:45", "10:00:00"), and 12-hour with AM/PM, any case, with or without
    /// a space and with or without minutes ("10:00 AM", "1:30pm", "10 AM"; 12:00 PM is noon and
    /// 12:00 AM is midnight). Hours must be 0-23 and minutes 0-59 (hours 1-12 with AM/PM). The
    /// scheduling limits (07:30 to 22:00) are not applied here.
    /// </summary>
    /// <param name="text">The text as typed. May be null.</param>
    /// <returns>Minutes from midnight, or an error when the text is blank or not a valid time.</returns>
    public static ParseResult<int> ParseTime(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return ParseResult<int>.Fail($"Enter {TimeFormsHint}.");

        int? minutes = null;

        var meridiem = MeridiemTime.Match(trimmed);
        if (meridiem.Success)
        {
            minutes = FromTwelveHour(
                int.Parse(meridiem.Groups[1].Value),
                meridiem.Groups[2].Success ? int.Parse(meridiem.Groups[2].Value) : 0,
                string.Equals(meridiem.Groups[3].Value, "pm", StringComparison.OrdinalIgnoreCase));
        }
        else if (CompactTime.Match(trimmed) is { Success: true } compact)
        {
            minutes = FromTwentyFourHour(int.Parse(compact.Groups[1].Value), int.Parse(compact.Groups[2].Value));
        }
        else if (ColonTime.Match(trimmed) is { Success: true } colon)
        {
            minutes = FromTwentyFourHour(int.Parse(colon.Groups[1].Value), int.Parse(colon.Groups[2].Value));
        }

        return minutes is { } m
            ? ParseResult<int>.Ok(m)
            : ParseResult<int>.Fail($"\"{trimmed}\" isn't a time. Use {TimeFormsHint}.");
    }

    /// <summary>Converts a 24-hour hour and minute to minutes from midnight.</summary>
    /// <param name="hour">The hour, valid when 0-23.</param>
    /// <param name="minute">The minute, valid when 0-59.</param>
    /// <returns>Minutes from midnight, or null when the hour or minute is out of range.</returns>
    private static int? FromTwentyFourHour(int hour, int minute) =>
        hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? hour * 60 + minute : null;

    /// <summary>Converts a 12-hour clock reading to minutes from midnight (12 AM is midnight, 12 PM is noon).</summary>
    /// <param name="hour">The hour, valid when 1-12.</param>
    /// <param name="minute">The minute, valid when 0-59.</param>
    /// <param name="isPm">True for PM, false for AM.</param>
    /// <returns>Minutes from midnight, or null when the hour or minute is out of range.</returns>
    private static int? FromTwelveHour(int hour, int minute, bool isPm)
    {
        if (hour is < 1 or > 12 || minute is < 0 or > 59)
            return null;

        int hour24 = hour % 12 + (isPm ? 12 : 0);
        return hour24 * 60 + minute;
    }

    // ── Week lists and frequencies ────────────────────────────────────────────

    /// <summary>
    /// Parses a custom week list such as "6, 1,7" into the stored form: ascending, comma-joined, no
    /// spaces ("1,6,7"). Tokens are split on commas, trimmed, and empty entries ignored. The rules
    /// and messages are exactly those of the meeting editor's Custom frequency field, and the first
    /// bad token (left to right) decides the message:
    /// blank text; a token that is not a whole number or is below 1; a week listed twice.
    /// </summary>
    /// <param name="text">The text as typed. May be null.</param>
    /// <returns>The stored week list, or an error.</returns>
    public static ParseResult<string> ParseWeekList(string? text)
    {
        var tokens = (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // No usable tokens ("", "  " or ",,") is treated as blank.
        if (tokens.Length == 0)
            return ParseResult<string>.Fail("Enter week numbers, e.g. 1,3,5");

        var weeks = new SortedSet<int>();
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var week) || week < 1)
                return ParseResult<string>.Fail("Each week must be a whole number ≥ 1");
            if (!weeks.Add(week))
                return ParseResult<string>.Fail("Duplicate week numbers");
        }

        return ParseResult<string>.Ok(string.Join(",", weeks));
    }

    /// <summary>
    /// Parses the frequency of a meeting into the value stored on
    /// <see cref="TermPoint.Models.SectionDaySchedule.Frequency"/>.
    /// <para>
    /// Blank or "weekly" mean every week (null). "odd" / "odd weeks" give "odd" and "even" /
    /// "even weeks" give "even" (any case; extra spaces ignored). Anything else must be a week list
    /// (see <see cref="ParseWeekList"/>). Text made only of digits, commas and spaces is clearly meant
    /// as a week list, so it gets the week-list message; any other text gets a message naming the
    /// accepted forms.
    /// </para>
    /// </summary>
    /// <param name="text">The text as typed. May be null.</param>
    /// <returns>
    /// The stored frequency (null for weekly, "odd", "even", or a week list like "1,6,7") or an error.
    /// A valid result with a null value means weekly.
    /// </returns>
    public static ParseResult<string?> ParseFrequency(string? text)
    {
        var normalized = NameMatch.Normalize(text);
        if (normalized.Length == 0)
            return ParseResult<string?>.Ok(null);

        switch (normalized.ToLowerInvariant())
        {
            case "weekly": return ParseResult<string?>.Ok(null);
            case "odd" or "odd weeks": return ParseResult<string?>.Ok("odd");
            case "even" or "even weeks": return ParseResult<string?>.Ok("even");
        }

        if (normalized.All(c => char.IsAsciiDigit(c) || c == ',' || c == ' '))
        {
            var weeks = ParseWeekList(normalized);
            return weeks.IsValid ? ParseResult<string?>.Ok(weeks.Value) : ParseResult<string?>.Fail(weeks.Error!);
        }

        return ParseResult<string?>.Fail(
            $"\"{normalized}\" isn't a frequency. Leave it blank for weekly, or use odd, even, or week numbers like 1,6,7.");
    }
}
