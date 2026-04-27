using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISubjectRepository"/> backed by a
/// mutable copy of <see cref="DemoData.Subjects"/>. All CRUD operations update the
/// in-memory list; changes are lost on page reload.
/// </summary>
public class DemoSubjectRepository : ISubjectRepository
{
    private readonly List<Subject> _subjects = [.. DemoData.Subjects];

    /// <inheritdoc/>
    public List<Subject> GetAll() =>
        [.. _subjects.OrderBy(s => s.Name)];

    /// <inheritdoc/>
    public Subject? GetById(string id) =>
        _subjects.FirstOrDefault(s => s.Id == id);

    /// <inheritdoc/>
    public bool HasCourses(string subjectId) =>
        DemoData.Courses.Any(c => c.SubjectId == subjectId);

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) =>
        _subjects.Any(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) &&
            s.Id != excludeId);

    /// <inheritdoc/>
    public bool ExistsByAbbreviation(string abbreviation, string? excludeId = null) =>
        _subjects.Any(s =>
            string.Equals(s.CalendarAbbreviation, abbreviation, StringComparison.OrdinalIgnoreCase) &&
            s.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Subject subject) => _subjects.Add(subject);

    /// <inheritdoc/>
    public void Update(Subject subject)
    {
        int i = _subjects.FindIndex(s => s.Id == subject.Id);
        if (i >= 0) _subjects[i] = subject;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _subjects.RemoveAll(s => s.Id == id);
}
