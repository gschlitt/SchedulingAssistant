using System.Globalization;
using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// What validation computes for one new section, stored on its <see cref="SectionDraft"/> for the
/// writer: the final capacity, the tag ids, the level and the day-by-day meetings.
/// </summary>
/// <param name="Capacity">The section's capacity (design D1), or null when none applies.</param>
/// <param name="TagIds">The ids of the file's tags followed by the course's own tags (design D2), without repeats.</param>
/// <param name="Level">The course's level, or empty.</param>
/// <param name="Meetings">One meeting per day per valid meeting row.</param>
internal sealed record SectionValues(
    int? Capacity,
    IReadOnlyList<string> TagIds,
    string Level,
    IReadOnlyList<MeetingDraft> Meetings);

/// <summary>
/// Validates one new section and computes its final values (the "validate" half of the planner,
/// step 24.4): its rows agree (invariant 4), its Capacity is a whole number, and its meetings follow
/// the meeting-time rules (<see cref="MeetingRules"/>). Every problem is collected, not just the first.
/// Pure: reads only its arguments and the snapshot.
/// </summary>
internal sealed class SectionValidator
{
    /// <summary>
    /// The section-level columns whose cells must agree across a section's rows, and whether each holds
    /// a semicolon-separated list (compared as a set, so a different order is not a difference).
    /// </summary>
    private static readonly (string Column, bool IsList)[] AgreementColumns =
    {
        (SectionImportColumns.SectionType, false),
        (SectionImportColumns.Instructors, true),
        (SectionImportColumns.Tags, true),
        (SectionImportColumns.Capacity, false),
        (SectionImportColumns.Campus, false),
    };

    private readonly ImportSetupSnapshot _snapshot;
    private readonly List<ImportProblem> _problems;
    private readonly MeetingRules _meetingRules;

    /// <summary>Creates a validator over a snapshot, reporting into a problem list.</summary>
    /// <param name="snapshot">Supplies the default capacity and the weekend settings.</param>
    /// <param name="problems">Where problems are added.</param>
    public SectionValidator(ImportSetupSnapshot snapshot, List<ImportProblem> problems)
    {
        _snapshot = snapshot;
        _problems = problems;
        _meetingRules = new MeetingRules(snapshot, problems);
    }

    /// <summary>
    /// Validates a new section and computes its final values.
    /// </summary>
    /// <param name="key">The section's key as typed on its first row (used in messages).</param>
    /// <param name="rows">The section's rows in file order.</param>
    /// <param name="course">The section's course, existing or new.</param>
    /// <param name="fileTags">The tags the file lists, already resolved.</param>
    /// <param name="meetingRows">The section's rows with their meeting text and resolved meeting type and room.</param>
    /// <returns>The computed values (meetings are empty for rows with problems).</returns>
    public SectionValues Validate(
        SectionKey key,
        IReadOnlyList<ImportRow> rows,
        CourseRef course,
        IReadOnlyList<SetupRef<SchedulingEnvironmentValue>> fileTags,
        IReadOnlyList<MeetingRowDraft> meetingRows)
    {
        CheckRowsAgree(key, rows);

        return new SectionValues(
            ResolveCapacity(rows, course.Record),
            MergeTagIds(fileTags, course.Record),
            course.Record.Level ?? string.Empty,
            meetingRows.SelectMany(_meetingRules.Expand).ToList());
    }

    // ── Invariant 4: a section's rows agree ───────────────────────────────────

    /// <summary>
    /// Compares each section-level cell with the first row where its column is filled; a later row that
    /// differs is a problem on that row. Blank cells are fine. Instructor(s) and Tags are compared as
    /// sets of entries, so listing them in a different order is not a difference.
    /// </summary>
    /// <param name="key">The section's key as typed on its first row.</param>
    /// <param name="rows">The section's rows in file order.</param>
    private void CheckRowsAgree(SectionKey key, IReadOnlyList<ImportRow> rows)
    {
        foreach (var (column, isList) in AgreementColumns)
        {
            if (ImportRowText.FirstNonBlank(rows, column) is not { } first)
                continue;

            var firstEntries = isList ? EntrySet(first.Text) : null;
            foreach (var row in rows.Where(r => r.RowNumber > first.Row.RowNumber))
            {
                var text = row.Cell(column);
                if (text.Length == 0)
                    continue;

                var same = isList ? firstEntries!.SetEquals(EntrySet(text)) : NameMatch.Equal(first.Text, text);
                if (!same)
                    _problems.Add(new ImportProblem(row.RowNumber, column,
                        $"{key.Label} has a different {column} than row {first.Row.RowNumber}."));
            }
        }
    }

    /// <summary>Splits a semicolon-separated cell into the set of its normalised, non-empty entries.</summary>
    /// <param name="cell">The cell text.</param>
    /// <returns>The entries, compared ignoring case and spacing.</returns>
    private static HashSet<string> EntrySet(string cell) =>
        new(cell.Split(';').Select(NameMatch.Normalize).Where(e => e.Length > 0), NameMatch.Comparer);

    // ── Capacity (D1) ─────────────────────────────────────────────────────────

    /// <summary>
    /// The section's capacity. The file's number, taken from the first row where Capacity is filled,
    /// must be a whole number of zero or more (a problem on that row otherwise); when it is blank or
    /// unusable the course's capacity applies, then the default section capacity, then none.
    /// </summary>
    /// <param name="rows">The section's rows in file order.</param>
    /// <param name="course">The section's course.</param>
    /// <returns>The capacity, or null when none applies.</returns>
    private int? ResolveCapacity(IReadOnlyList<ImportRow> rows, Course course)
    {
        if (ImportRowText.FirstNonBlank(rows, SectionImportColumns.Capacity) is { } hit)
        {
            // NumberStyles.None: digits only, so a sign, a decimal point or a thousands separator is refused.
            if (int.TryParse(hit.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var capacity))
                return capacity;

            _problems.Add(new ImportProblem(hit.Row.RowNumber, SectionImportColumns.Capacity,
                "Capacity must be a whole number, like 30."));
        }
        return course.Capacity ?? _snapshot.DefaultSectionCapacity;
    }

    // ── Tags (D2) ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The file's tags (their ids) followed by the course's own tags that are not already included,
    /// as the section editor does for a new section. New tags already carry their ids.
    /// </summary>
    /// <param name="fileTags">The tags the file lists.</param>
    /// <param name="course">The section's course (a new course has none).</param>
    /// <returns>The ids, file tags first, each once.</returns>
    private static IReadOnlyList<string> MergeTagIds(
        IReadOnlyList<SetupRef<SchedulingEnvironmentValue>> fileTags, Course course) =>
        fileTags.Select(t => t.Record.Id).Concat(course.TagIds).Distinct().ToList();
}
