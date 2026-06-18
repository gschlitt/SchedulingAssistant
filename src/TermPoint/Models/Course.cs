using System.Text.Json.Serialization;

namespace TermPoint.Models;

public class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string CalendarCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// IDs of tags (from SchedulingEnvironmentValues of type "tag") associated with this course.
    /// These are automatically applied to new sections created for this course.
    /// </summary>
    public List<string> TagIds { get; set; } = new();

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Human-readable tag names for display in lists. Populated by the ViewModel after loading;
    /// not persisted to the database.
    /// </summary>
    [JsonIgnore]
    public string TagSummary { get; set; } = string.Empty;

    /// <summary>
    /// The course level band, e.g. "100", "200", "300".
    /// Set by the administrator in the course editor; auto-suggested from the course
    /// number by <see cref="Services.CourseLevelParser.ParseLevel"/> but may be
    /// overridden or left empty.  Stored in the JSON data column.
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// Optional seating capacity for the course. Null means "not specified" — never treat null as 0.
    /// New sections of this course inherit it as their starting capacity (see SectionEditViewModel).
    /// </summary>
    public int? Capacity { get; set; }
}
