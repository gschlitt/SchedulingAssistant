using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Display model for one course row in the CSV import preview list.
/// Immutable after construction — no operator interaction needed
/// (courses are either new or matched; no ambiguity tier).
/// </summary>
public class CoursePreviewRow
{
    /// <summary>Calendar code from the CSV, e.g. "CHEM 101".</summary>
    public string CalendarCode { get; }

    /// <summary>Course title from the CSV (may be empty).</summary>
    public string Title { get; }

    /// <summary>Exact or Unmatched — no ambiguity for courses.</summary>
    public MatchStatus Status { get; }

    /// <summary>Display name of the resolved subject, or "(unmapped)" if rejected.</summary>
    public string SubjectDisplay { get; }

    /// <summary>The resolved subject ID, or null if the subject couldn't be mapped.</summary>
    public string? SubjectId { get; }

    /// <summary>The original parsed CSV row, used for entity creation on import.</summary>
    public CourseRow Row { get; }

    /// <summary>The existing database course this row matched (Exact status only).</summary>
    public Course? ExactMatch { get; }

    /// <summary>True when the subject mapping failed — this row will be rejected on import.</summary>
    public bool IsRejected { get; }

    /// <summary>Status label for the preview: "(new)", "(matched)", or "(rejected)".</summary>
    public string StatusLabel => IsRejected ? "(rejected — no subject)"
        : Status == MatchStatus.Exact ? "(matched)" : "(new)";

    public CoursePreviewRow(CourseRow row, MatchResult<Course> courseMatch, string? subjectId, string subjectDisplay)
    {
        Row = row;
        CalendarCode = row.CalendarCode.Trim();
        Title = row.Title.Trim();
        Status = courseMatch.Status;
        ExactMatch = courseMatch.Status == MatchStatus.Exact ? courseMatch.Resolved : null;
        SubjectId = subjectId;
        SubjectDisplay = subjectDisplay;
        IsRejected = string.IsNullOrEmpty(subjectId);
    }
}
