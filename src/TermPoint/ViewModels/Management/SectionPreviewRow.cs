using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Display model for one section row in the CSV import preview list.
/// Immutable after construction — resolved references (semester, course,
/// instructors, schedule) are baked in at creation time.
/// </summary>
public class SectionPreviewRow
{
    /// <summary>The original parsed CSV row, used for entity creation on import.</summary>
    public SectionRow Row { get; }

    /// <summary>Calendar code from the CSV, e.g. "CHEM201".</summary>
    public string CourseCode { get; }

    /// <summary>Section code from the CSV, e.g. "A" or "AB1".</summary>
    public string SectionCode { get; }

    /// <summary>Resolved semester ID, or null if the semester could not be resolved.</summary>
    public string? SemesterId { get; }

    /// <summary>Resolved course ID, or null if the course could not be matched.</summary>
    public string? CourseId { get; }

    /// <summary>Level band from the matched course (e.g. "100", "300"), copied to the section for filtering.</summary>
    public string? CourseLevel { get; }

    /// <summary>Display text for instructors — resolved names or CSV text with warning markers.</summary>
    public string InstructorDisplay { get; }

    /// <summary>Compact meeting schedule summary, e.g. "Mon 8:00 AM (50 min); Wed 10:00 AM (80 min)".</summary>
    public string MeetingSummary { get; }

    /// <summary>Resolved instructor assignments (Workload = 1.0 each).</summary>
    public List<InstructorAssignment> ResolvedInstructors { get; }

    /// <summary>Converted schedule entries built from MeetingRows + environment mappings.</summary>
    public List<SectionDaySchedule> ResolvedSchedule { get; }

    /// <summary>Warnings for unresolved references (e.g. "Instructor 'J. Doe' not found").</summary>
    public List<string> Warnings { get; }

    /// <summary>True when the section cannot be imported (missing semester/course, duplicate code).</summary>
    public bool IsRejected { get; }

    /// <summary>Why the section was rejected (empty when not rejected).</summary>
    public string RejectionReason { get; }

    /// <summary>Status label for the preview list.</summary>
    public string StatusLabel => IsRejected
        ? $"(rejected — {RejectionReason})"
        : Warnings.Count > 0
            ? $"(new, {Warnings.Count} warning(s))"
            : "(new)";

    /// <summary>True when warnings exist but the section is still importable.</summary>
    public bool HasWarnings => Warnings.Count > 0 && !IsRejected;

    /// <summary>Joined warnings text for display.</summary>
    public string WarningsText => string.Join("; ", Warnings);

    /// <param name="row">Original parsed section row.</param>
    /// <param name="semesterId">Resolved semester ID, or null.</param>
    /// <param name="courseId">Resolved course ID, or null.</param>
    /// <param name="courseLevel">Level band from the matched course, or null.</param>
    /// <param name="instructorDisplay">Formatted instructor display text.</param>
    /// <param name="meetingSummary">Formatted meeting schedule text.</param>
    /// <param name="resolvedInstructors">Matched instructor assignments.</param>
    /// <param name="resolvedSchedule">Converted schedule entries.</param>
    /// <param name="warnings">Non-fatal reference resolution warnings.</param>
    /// <param name="rejectionReason">Null or empty if not rejected; reason text otherwise.</param>
    public SectionPreviewRow(
        SectionRow row,
        string? semesterId,
        string? courseId,
        string? courseLevel,
        string instructorDisplay,
        string meetingSummary,
        List<InstructorAssignment> resolvedInstructors,
        List<SectionDaySchedule> resolvedSchedule,
        List<string> warnings,
        string? rejectionReason)
    {
        Row = row;
        CourseCode = row.CourseCode.Trim();
        SectionCode = row.SectionCode.Trim();
        SemesterId = semesterId;
        CourseId = courseId;
        CourseLevel = courseLevel;
        InstructorDisplay = instructorDisplay;
        MeetingSummary = meetingSummary;
        ResolvedInstructors = resolvedInstructors;
        ResolvedSchedule = resolvedSchedule;
        Warnings = warnings;
        RejectionReason = rejectionReason ?? string.Empty;
        IsRejected = !string.IsNullOrEmpty(rejectionReason);
    }
}
