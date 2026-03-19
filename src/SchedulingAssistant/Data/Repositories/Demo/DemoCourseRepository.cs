using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ICourseRepository"/> backed by
/// <see cref="DemoData.Courses"/>.  Write operations are no-ops.
/// </summary>
public class DemoCourseRepository : ICourseRepository
{
    /// <inheritdoc/>
    public List<Course> GetAll() =>
        [.. DemoData.Courses.OrderBy(c => c.CalendarCode)];

    /// <inheritdoc/>
    public List<Course> GetBySubject(string subjectId) =>
        [.. DemoData.Courses.Where(c => c.SubjectId == subjectId).OrderBy(c => c.CalendarCode)];

    /// <inheritdoc/>
    public List<Course> GetAllActive() =>
        [.. DemoData.Courses.Where(c => c.IsActive).OrderBy(c => c.CalendarCode)];

    /// <inheritdoc/>
    public Course? GetById(string id) =>
        DemoData.Courses.FirstOrDefault(c => c.Id == id);

    /// <inheritdoc/>
    public bool HasSections(string courseId) =>
        DemoData.Sections.Any(s => s.CourseId == courseId);

    /// <inheritdoc/>
    public bool ExistsByCalendarCode(string code, string? excludeId = null) =>
        DemoData.Courses.Any(c =>
            string.Equals(c.CalendarCode, code, StringComparison.OrdinalIgnoreCase) &&
            c.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Course course) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(Course course, DbTransaction? tx = null) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id) { /* no-op in demo */ }
}
