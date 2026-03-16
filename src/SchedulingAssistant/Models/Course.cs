namespace SchedulingAssistant.Models;

public class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string CalendarCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>IDs of tags (from SectionPropertyValues) attached to this course.</summary>
    public List<string> TagIds { get; set; } = new();

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Transient display string (not persisted). Populated by CourseListViewModel after
    /// loading so that DataGrid rows can show tag names without injecting a repository.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TagSummary { get; set; } = string.Empty;

    /// <summary>
    /// Computes the course level from the calendar code.
    /// Levels are "0XX", "1XX", "2XX", "3XX", "4XX", or "5+XX" based on the hundreds digit of the course number.
    /// For example: "HIST101" → "1XX", "MATH205" → "2XX", "CHEM599" → "5+XX".
    /// </summary>
    public string Level
    {
        get
        {
            if (string.IsNullOrEmpty(CalendarCode))
                return string.Empty;

            // Extract numeric suffix (e.g., "101" from "HIST101")
            var numericPart = new string(CalendarCode.SkipWhile(c => !char.IsDigit(c)).ToArray());
            if (!int.TryParse(numericPart, out var courseNum))
                return string.Empty;

            int levelDigit = courseNum / 100;  // Get hundreds digit
            if (levelDigit >= 5)
                return "5+XX";
            else
                return $"{levelDigit}XX";
        }
    }
}
