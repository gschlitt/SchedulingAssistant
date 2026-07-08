using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Matches parsed CSV Import rows against existing database records. Stateless — every
/// result is returned via <see cref="MatchResult{T}"/>. Pure functions: no repository or
/// database access; callers load records via the relevant <c>GetAll()</c> method and pass
/// them in.
/// </summary>
public class CsvImportMatcher
{
    private static readonly string[] Honorifics = { "Dr.", "Dr", "Mr.", "Mr", "Mrs.", "Mrs", "Ms.", "Ms", "Prof.", "Prof", "Professor" };

    /// <summary>
    /// Builds an instructor match index from existing database instructors, keyed by
    /// normalized last name (honorifics stripped, whitespace collapsed, case-insensitive).
    /// </summary>
    public InstructorMatchIndex BuildInstructorIndex(List<Instructor> existing)
    {
        var byLastName = new Dictionary<string, List<Instructor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var instructor in existing)
        {
            var key = NormalizeName(instructor.LastName);
            if (!byLastName.TryGetValue(key, out var list))
            {
                list = new List<Instructor>();
                byLastName[key] = list;
            }
            list.Add(instructor);
        }
        return new InstructorMatchIndex(byLastName);
    }

    /// <summary>
    /// Matches an imported instructor row against the index, in tier order: exact
    /// (last + first name), last-only (CSV first name blank), fuzzy (CSV first name is an
    /// initial compatible with a candidate's first name), then ambiguous (last name matches
    /// but the candidates can't be narrowed to one). Honorifics are stripped from both sides
    /// before comparing.
    /// </summary>
    public MatchResult<Instructor> MatchInstructor(InstructorRow row, InstructorMatchIndex index)
    {
        var displayName = $"{row.FirstName} {row.LastName}".Trim();
        var lastKey = NormalizeName(row.LastName);

        if (!index.ByLastName.TryGetValue(lastKey, out var candidates) || candidates.Count == 0)
            return new MatchResult<Instructor> { CsvValue = displayName, Status = MatchStatus.Unmatched };

        var csvFirst = NormalizeName(row.FirstName);

        // Tier 2: last-only — CSV supplies no first name to disambiguate.
        if (string.IsNullOrEmpty(csvFirst))
        {
            return candidates.Count == 1
                ? new MatchResult<Instructor> { CsvValue = displayName, Status = MatchStatus.Exact, Resolved = candidates[0] }
                : new MatchResult<Instructor> { CsvValue = displayName, Status = MatchStatus.Ambiguous, Candidates = candidates };
        }

        // Tier 1: exact — first name matches one candidate exactly.
        var exact = candidates.Where(c => NormalizeName(c.FirstName).Equals(csvFirst, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return new MatchResult<Instructor> { CsvValue = displayName, Status = MatchStatus.Exact, Resolved = exact[0] };
        if (exact.Count > 1)
            return new MatchResult<Instructor> { CsvValue = displayName, Status = MatchStatus.Ambiguous, Candidates = exact };

        // Tier 3: fuzzy — CSV first name is an initial compatible with a candidate's first name.
        var fuzzy = candidates.Where(c => IsInitialCompatible(csvFirst, NormalizeName(c.FirstName))).ToList();
        if (fuzzy.Count == 1)
            return new MatchResult<Instructor> { CsvValue = displayName, Status = MatchStatus.Exact, Resolved = fuzzy[0] };

        // Tier 4: ambiguous — last name matched, but the first name can't narrow it to one
        // record (no compatible candidate, or more than one). Flag for manual resolution
        // rather than guessing, even when only one same-last-name candidate exists.
        return new MatchResult<Instructor>
        {
            CsvValue = displayName,
            Status = MatchStatus.Ambiguous,
            Candidates = fuzzy.Count > 1 ? fuzzy : candidates,
        };
    }

    /// <summary>Matches a CalendarCode against existing courses (case-insensitive, whitespace-normalized).</summary>
    public MatchResult<Course> MatchCourse(string calendarCode, Dictionary<string, Course> courseIndex)
    {
        if (string.IsNullOrWhiteSpace(calendarCode))
            return new MatchResult<Course> { CsvValue = calendarCode, Status = MatchStatus.Skipped };

        var key = NormalizeWhitespace(calendarCode);
        return courseIndex.TryGetValue(key, out var course)
            ? new MatchResult<Course> { CsvValue = calendarCode, Status = MatchStatus.Exact, Resolved = course }
            : new MatchResult<Course> { CsvValue = calendarCode, Status = MatchStatus.Unmatched };
    }

    /// <summary>Matches a SubjectCode against existing subjects by CalendarAbbreviation (case-insensitive).</summary>
    public MatchResult<Subject> MatchSubject(string subjectCode, Dictionary<string, Subject> subjectIndex)
    {
        if (string.IsNullOrWhiteSpace(subjectCode))
            return new MatchResult<Subject> { CsvValue = subjectCode, Status = MatchStatus.Skipped };

        var key = NormalizeWhitespace(subjectCode);
        return subjectIndex.TryGetValue(key, out var subject)
            ? new MatchResult<Subject> { CsvValue = subjectCode, Status = MatchStatus.Exact, Resolved = subject }
            : new MatchResult<Subject> { CsvValue = subjectCode, Status = MatchStatus.Unmatched };
    }

    /// <summary>
    /// Matches a CSV string value (Room, SectionType, Campus, or MeetingType) against a list
    /// of pre-configured environment records by display name (case-insensitive,
    /// whitespace-normalized). Used both for simple name matches (SectionType, Campus,
    /// MeetingType) and for Room's composite "{Building} {RoomNumber}" display name — the
    /// caller builds that composite when constructing the EnvironmentTarget list.
    /// </summary>
    public MatchResult<EnvironmentTarget> MatchEnvironmentValue(string csvValue, List<EnvironmentTarget> dbValues)
    {
        if (string.IsNullOrWhiteSpace(csvValue))
            return new MatchResult<EnvironmentTarget> { CsvValue = csvValue, Status = MatchStatus.Skipped };

        var key = NormalizeWhitespace(csvValue);
        var matches = dbValues.Where(v => NormalizeWhitespace(v.DisplayName).Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();

        return matches.Count switch
        {
            1 => new MatchResult<EnvironmentTarget> { CsvValue = csvValue, Status = MatchStatus.Exact, Resolved = matches[0] },
            0 => new MatchResult<EnvironmentTarget> { CsvValue = csvValue, Status = MatchStatus.Unmatched },
            _ => new MatchResult<EnvironmentTarget> { CsvValue = csvValue, Status = MatchStatus.Ambiguous, Candidates = matches },
        };
    }

    /// <summary>True when <paramref name="csvFirst"/> is a single-letter initial (with or without a trailing period) matching the first letter of <paramref name="dbFirst"/>.</summary>
    private static bool IsInitialCompatible(string csvFirst, string dbFirst)
    {
        var initial = csvFirst.TrimEnd('.');
        return initial.Length == 1 && dbFirst.Length > 0 &&
               char.ToUpperInvariant(initial[0]) == char.ToUpperInvariant(dbFirst[0]);
    }

    /// <summary>Strips a leading honorific, then collapses whitespace. Used for instructor name comparison.</summary>
    private static string NormalizeName(string? value)
    {
        var s = (value ?? string.Empty).Trim();
        foreach (var honorific in Honorifics)
        {
            if (s.StartsWith(honorific + " ", StringComparison.OrdinalIgnoreCase))
            {
                s = s[honorific.Length..].TrimStart();
                break;
            }
        }
        return NormalizeWhitespace(s);
    }

    /// <summary>Collapses runs of internal whitespace to single spaces and trims. Used for code/name comparison.</summary>
    private static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Existing instructors grouped by normalized last name, for matching imported
/// InstructorRows against the database.
/// </summary>
public class InstructorMatchIndex
{
    /// <summary>Key: normalized (honorific-stripped, whitespace-collapsed) last name. Value: all instructors sharing that last name.</summary>
    public Dictionary<string, List<Instructor>> ByLastName { get; }

    public InstructorMatchIndex(Dictionary<string, List<Instructor>> byLastName) => ByLastName = byLastName;
}
