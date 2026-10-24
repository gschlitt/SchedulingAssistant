using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// One row of a section, as far as step 24.3 takes it: the meeting-level text left raw (step 24.4
/// turns it into day-by-day meetings) and the meeting type and room already resolved.
/// </summary>
/// <param name="RowNumber">The row number as Excel shows it.</param>
/// <param name="RawDays">The Meeting Days cell, trimmed; empty when blank.</param>
/// <param name="RawBegin">The Begin cell, trimmed; empty when blank.</param>
/// <param name="RawEnd">The End cell, trimmed; empty when blank.</param>
/// <param name="RawFrequency">The Frequency cell, trimmed; empty when blank (weekly).</param>
/// <param name="MeetingType">The resolved meeting type, or null when the cell is blank (or could not be resolved).</param>
/// <param name="Room">The resolved room, or null when there is none (or it could not be resolved).</param>
public sealed record MeetingRowDraft(
    int RowNumber,
    string RawDays,
    string RawBegin,
    string RawEnd,
    string RawFrequency,
    SetupRef<SchedulingEnvironmentValue>? MeetingType,
    SetupRef<Room>? Room);

/// <summary>
/// One section of the import file: all the rows that share a <see cref="SectionKey"/>, with every
/// value they name resolved to an existing or a new record, plus (step 24.4) its final capacity, tag
/// ids, level and day-by-day meetings, computed from the raw text kept here.
/// <para>
/// A draft for a section that is already in the semester has <see cref="AlreadyExists"/> set and
/// resolves nothing beyond its course: it is skipped whole and its other cells are never looked at.
/// </para>
/// </summary>
public sealed class SectionDraft
{
    /// <summary>The section's key, spelled as on its first row.</summary>
    public required SectionKey Key { get; init; }

    /// <summary>The row numbers of the section's rows, in file order.</summary>
    public required IReadOnlyList<int> RowNumbers { get; init; }

    /// <summary>
    /// The section's rows in file order, with their raw cells. Step 24.4 reads these for the
    /// "section rows must agree" check and for each row's Course Title (see <see cref="CellsByRow"/>).
    /// </summary>
    public required IReadOnlyList<ImportRow> Rows { get; init; }

    /// <summary>True when the section is already in the open semester, so it is skipped.</summary>
    public bool AlreadyExists { get; init; }

    /// <summary>The section's course (existing or new).</summary>
    public required CourseRef Course { get; init; }

    /// <summary>The instructors, in the order the file lists them; empty when unstaffed (or for a skipped section).</summary>
    public IReadOnlyList<InstructorRef> Instructors { get; init; } = Array.Empty<InstructorRef>();

    /// <summary>The section type, or null when the cell is blank.</summary>
    public SetupRef<SchedulingEnvironmentValue>? SectionType { get; init; }

    /// <summary>The campus, or null when the cell is blank.</summary>
    public SetupRef<Campus>? Campus { get; init; }

    /// <summary>The tags the file lists, in file order; empty when blank. The course's own tags (D2) are added later.</summary>
    public IReadOnlyList<SetupRef<SchedulingEnvironmentValue>> Tags { get; init; } =
        Array.Empty<SetupRef<SchedulingEnvironmentValue>>();

    /// <summary>
    /// The Capacity cell text, trimmed, taken from the first row where it is filled; null when no row
    /// fills it. Step 24.4 parses it and applies D1.
    /// </summary>
    public string? RawCapacity { get; init; }

    /// <summary>One entry per row, in file order; empty for a skipped section.</summary>
    public IReadOnlyList<MeetingRowDraft> MeetingRows { get; init; } = Array.Empty<MeetingRowDraft>();

    /// <summary>
    /// The section's final capacity (step 24.4, design D1): the file's number; when blank, the
    /// course's capacity, else the default section capacity, else null.
    /// </summary>
    public int? Capacity { get; init; }

    /// <summary>
    /// The ids of the section's tags (design D2): the file's tags first, then the course's own tags
    /// that are not already included, without repeats.
    /// </summary>
    public IReadOnlyList<string> TagIds { get; init; } = Array.Empty<string>();

    /// <summary>The section's level, copied from its course as the editor does; empty for a new course.</summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>
    /// The section's meetings, one per day per valid <see cref="MeetingRows"/> entry. A row with a
    /// problem makes none (the problem stops the import anyway).
    /// </summary>
    public IReadOnlyList<MeetingDraft> Meetings { get; init; } = Array.Empty<MeetingDraft>();

    /// <summary>
    /// The (row number, trimmed text) of one column across the section's rows, in file order,
    /// including blanks. Step 24.4 uses it for the agreement check and for Course Title.
    /// </summary>
    /// <param name="column">A template column name (see <see cref="SectionImportColumns"/>).</param>
    /// <returns>One pair per row.</returns>
    public IEnumerable<(int Row, string Text)> CellsByRow(string column) =>
        Rows.Select(r => (r.RowNumber, r.Cell(column)));
}
