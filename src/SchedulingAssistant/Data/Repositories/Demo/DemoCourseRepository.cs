using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ICourseRepository"/> backed by a
/// mutable copy of <see cref="DemoData.Courses"/>. All CRUD operations update the
/// in-memory list; changes are lost on page reload.
/// </summary>
public class DemoCourseRepository : ICourseRepository
{
    private readonly List<Course> _courses = [.. DemoData.Courses];

    /// <inheritdoc/>
    public List<Course> GetAll() =>
        [.. _courses.OrderBy(c => c.CalendarCode)];

    /// <inheritdoc/>
    public List<Course> GetBySubject(string subjectId) =>
        [.. _courses.Where(c => c.SubjectId == subjectId).OrderBy(c => c.CalendarCode)];

    /// <inheritdoc/>
    public List<Course> GetAllActive() =>
        [.. _courses.Where(c => c.IsActive).OrderBy(c => c.CalendarCode)];

    /// <inheritdoc/>
    public Course? GetById(string id) =>
        _courses.FirstOrDefault(c => c.Id == id);

    /// <inheritdoc/>
    public bool HasSections(string courseId) =>
        DemoData.Sections.Any(s => s.CourseId == courseId);

    /// <inheritdoc/>
    public bool ExistsByCalendarCode(string code, string? excludeId = null) =>
        _courses.Any(c =>
            string.Equals(c.CalendarCode, code, StringComparison.OrdinalIgnoreCase) &&
            c.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Course course) => _courses.Add(course);

    /// <inheritdoc/>
    public void Update(Course course, DbTransaction? tx = null)
    {
        int i = _courses.FindIndex(c => c.Id == course.Id);
        if (i >= 0) _courses[i] = course;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _courses.RemoveAll(c => c.Id == id);
}
