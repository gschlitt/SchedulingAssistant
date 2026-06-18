namespace TermPoint.Models;

/// <summary>
/// A free-text scheduling note attached to a specific instructor within a specific semester.
/// Exactly one note exists per (instructor, semester) pair; it is created lazily the first
/// time text is saved and updated in place thereafter.
/// </summary>
public class SchedulingNote
{
    /// <summary>Stable surrogate key for the note row.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The instructor this note belongs to.</summary>
    public string InstructorId { get; set; } = string.Empty;

    /// <summary>The semester this note applies to.</summary>
    public string SemesterId { get; set; } = string.Empty;

    /// <summary>The note body. May be empty.</summary>
    public string Text { get; set; } = string.Empty;
}
