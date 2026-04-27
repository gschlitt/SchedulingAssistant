using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IInstructorRepository"/> backed by a
/// mutable copy of <see cref="DemoData.Instructors"/>. All CRUD operations update the
/// in-memory list; changes are lost on page reload.
/// </summary>
public class DemoInstructorRepository : IInstructorRepository
{
    private readonly List<Instructor> _instructors = [.. DemoData.Instructors];

    /// <inheritdoc/>
    public List<Instructor> GetAll() =>
        [.. _instructors.OrderBy(i => i.LastName).ThenBy(i => i.FirstName)];

    /// <inheritdoc/>
    public Instructor? GetById(string id) =>
        _instructors.FirstOrDefault(i => i.Id == id);

    /// <inheritdoc/>
    public bool HasSections(string instructorId) =>
        DemoData.Sections.Any(s => s.InstructorAssignments.Any(a => a.InstructorId == instructorId));

    /// <inheritdoc/>
    public bool ExistsByInitials(string initials, string? excludeId = null) =>
        _instructors.Any(i =>
            string.Equals(i.Initials, initials, StringComparison.OrdinalIgnoreCase) &&
            i.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Instructor instructor) => _instructors.Add(instructor);

    /// <inheritdoc/>
    public void Update(Instructor instructor, DbTransaction? tx = null)
    {
        int i = _instructors.FindIndex(x => x.Id == instructor.Id);
        if (i >= 0) _instructors[i] = instructor;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _instructors.RemoveAll(i => i.Id == id);
}
