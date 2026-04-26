using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ISubjectRepository"/>
/// backed by <see cref="DemoData.Subjects"/>. Write operations are no-ops.
/// </summary>
public class DemoSubjectRepository : ISubjectRepository
{
    /// <inheritdoc/>
    public List<Subject> GetAll() =>
        [.. DemoData.Subjects.OrderBy(s => s.Name)];

    /// <inheritdoc/>
    public Subject? GetById(string id) =>
        DemoData.Subjects.FirstOrDefault(s => s.Id == id);

    /// <inheritdoc/>
    public bool HasCourses(string subjectId) =>
        DemoData.Courses.Any(c => c.SubjectId == subjectId);

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) =>
        DemoData.Subjects.Any(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) &&
            s.Id != excludeId);

    /// <inheritdoc/>
    public bool ExistsByAbbreviation(string abbreviation, string? excludeId = null) =>
        DemoData.Subjects.Any(s =>
            string.Equals(s.CalendarAbbreviation, abbreviation, StringComparison.OrdinalIgnoreCase) &&
            s.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Subject subject) { }

    /// <inheritdoc/>
    public void Update(Subject subject) { }

    /// <inheritdoc/>
    public void Delete(string id) { }
}
