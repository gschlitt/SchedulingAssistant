namespace TermPoint.Models;

/// <summary>
/// One row parsed from an imported instructors CSV, before matching against
/// existing Instructor records. In-memory only — never persisted.
/// </summary>
public class InstructorRow
{
    /// <summary>Required. Must be non-empty after trimming.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>May be blank — some listings show only last names.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>May be blank — auto-generated (with collision avoidance) on import if omitted.</summary>
    public string Initials { get; set; } = string.Empty;

    /// <summary>May be blank.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// One row parsed from an imported courses CSV, before matching against
/// existing Course records. In-memory only — never persisted.
/// </summary>
public class CourseRow
{
    /// <summary>
    /// Maps to a pre-created Subject via <c>Subject.CalendarAbbreviation</c>.
    /// May be blank — an unmapped or missing SubjectCode rejects the row at write time.
    /// </summary>
    public string SubjectCode { get; set; } = string.Empty;

    /// <summary>Required. Human-readable code, e.g. "CHEM 101". Must be non-empty after trimming.</summary>
    public string CalendarCode { get; set; } = string.Empty;

    /// <summary>May be blank.</summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// One scheduled meeting within an imported SectionRow. Fields are raw strings exactly
/// as they appeared in the CSV — day/time parsing into numeric form happens later, during
/// entity construction. In-memory only — never persisted.
/// </summary>
public class MeetingRow
{
    /// <summary>Raw day text (e.g. "Monday", "Mon", "M", or "1").</summary>
    public string Day { get; set; } = string.Empty;

    /// <summary>Raw start time text in "h:mm tt" form (e.g. "8:00 AM").</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>
    /// Raw end time text in "h:mm tt" form. Derived from StartTime + DurationMin
    /// by the parser if left blank in the CSV.
    /// </summary>
    public string EndTime { get; set; } = string.Empty;

    /// <summary>
    /// Raw duration in minutes, as text. Derived from StartTime + EndTime by the
    /// parser if left blank in the CSV.
    /// </summary>
    public string DurationMin { get; set; } = string.Empty;

    /// <summary>Raw room text (e.g. "Science 101"). May be blank.</summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>Raw frequency text (e.g. "odd", "1,6,7"). May be blank, meaning every week.</summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>Raw meeting type text (e.g. "Lecture"). May be blank.</summary>
    public string MeetingType { get; set; } = string.Empty;
}

/// <summary>
/// One section parsed from an imported sections CSV, grouped from a primary row plus
/// any continuation rows (blank CourseCode/SectionCode) that follow it. In-memory
/// only — never persisted.
/// </summary>
public class SectionRow
{
    /// <summary>Narrows Semester resolution (e.g. "2025-2026"). May be blank.</summary>
    public string AcademicYear { get; set; } = string.Empty;

    /// <summary>Required. Must resolve to an existing Semester record in the database.</summary>
    public string Semester { get; set; } = string.Empty;

    /// <summary>Required. Matched against Course.CalendarCode.</summary>
    public string CourseCode { get; set; } = string.Empty;

    /// <summary>May be blank. Informational only — not written to the Course record.</summary>
    public string CourseTitle { get; set; } = string.Empty;

    /// <summary>Required.</summary>
    public string SectionCode { get; set; } = string.Empty;

    /// <summary>Semicolon-separated "FirstName LastName" pairs.</summary>
    public string Instructors { get; set; } = string.Empty;

    /// <summary>Matched against a pre-configured SchedulingEnvironmentValue during import.</summary>
    public string SectionType { get; set; } = string.Empty;

    /// <summary>Matched against a pre-configured Campus during import.</summary>
    public string Campus { get; set; } = string.Empty;

    /// <summary>Semicolon-separated. Deferred — ignored by the import for now.</summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>Semicolon-separated. Deferred — ignored by the import for now.</summary>
    public string Resources { get; set; } = string.Empty;

    /// <summary>Semicolon-separated "Name (Code)" pairs. Deferred — ignored by the import for now.</summary>
    public string Reserves { get; set; } = string.Empty;

    /// <summary>One entry per meeting occurrence. Empty for unscheduled sections.</summary>
    public List<MeetingRow> Meetings { get; set; } = new();
}

/// <summary>Outcome of matching a single CSV value against existing database records.</summary>
public enum MatchStatus
{
    /// <summary>A single unambiguous database record was found.</summary>
    Exact,

    /// <summary>Multiple candidate records were found; the operator must pick one.</summary>
    Ambiguous,

    /// <summary>No matching database record was found.</summary>
    Unmatched,

    /// <summary>The CSV value was blank — no matching was attempted.</summary>
    Skipped,
}

/// <summary>
/// Result of matching one CSV value against existing database records of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The database record type being matched (e.g. Instructor, Course, EnvironmentTarget).</typeparam>
public class MatchResult<T> where T : class
{
    /// <summary>The raw value from the CSV that was matched.</summary>
    public string CsvValue { get; set; } = string.Empty;

    /// <summary>Outcome of the match attempt.</summary>
    public MatchStatus Status { get; set; }

    /// <summary>The matched database record, if <see cref="Status"/> is Exact. Null otherwise.</summary>
    public T? Resolved { get; set; }

    /// <summary>Populated with the candidate records when <see cref="Status"/> is Ambiguous.</summary>
    public List<T> Candidates { get; set; } = new();
}

/// <summary>
/// Wraps a pre-configured environment record (Room or SchedulingEnvironmentValue) for
/// mapping-table display, since the underlying database types differ.
/// </summary>
public class EnvironmentTarget
{
    /// <summary>Database record ID (GUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label shown in the mapping UI.</summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>One structural parse failure, tied to the CSV line number that caused it.</summary>
/// <param name="LineNumber">1-based line number within the CSV file.</param>
/// <param name="Message">Human-readable description of the failure.</param>
public record CsvParseError(int LineNumber, string Message);

/// <summary>Result of parsing one CSV file into typed rows. Malformed rows are skipped, not fatal.</summary>
/// <typeparam name="T">The parsed row type (InstructorRow, CourseRow, or SectionRow).</typeparam>
/// <param name="Rows">Successfully parsed rows.</param>
/// <param name="Errors">Structural parse failures, one per rejected row.</param>
public record CsvParseResult<T>(List<T> Rows, List<CsvParseError> Errors);

/// <summary>Summary of one import operation (instructors, courses, or sections), for the log panel.</summary>
public class ImportResult
{
    /// <summary>New records inserted.</summary>
    public int Created { get; set; }

    /// <summary>Rows that matched an existing record and were not re-created.</summary>
    public int Skipped { get; set; }

    /// <summary>Rows imported with one or more unresolved references (field left blank).</summary>
    public int Warnings { get; set; }

    /// <summary>Rows rejected outright (missing required fields, duplicates, etc.).</summary>
    public int Errors { get; set; }

    /// <summary>Timestamped, human-readable log entries describing what happened.</summary>
    public List<string> Log { get; set; } = new();
}
