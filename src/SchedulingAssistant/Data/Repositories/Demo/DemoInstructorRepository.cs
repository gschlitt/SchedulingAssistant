using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IInstructorRepository"/>
/// backed by <see cref="DemoData.Instructors"/>. Write operations are no-ops.
/// </summary>
public class DemoInstructorRepository : IInstructorRepository
{
    /// <inheritdoc/>
    public List<Instructor> GetAll() =>
        [.. DemoData.Instructors.OrderBy(i => i.LastName).ThenBy(i => i.FirstName)];

    /// <inheritdoc/>
    public Instructor? GetById(string id) =>
        DemoData.Instructors.FirstOrDefault(i => i.Id == id);

    /// <inheritdoc/>
    public bool HasSections(string instructorId) =>
        DemoData.Sections.Any(s => s.InstructorAssignments.Any(a => a.InstructorId == instructorId));

    /// <inheritdoc/>
    public bool ExistsByInitials(string initials, string? excludeId = null) =>
        DemoData.Instructors.Any(i =>
            string.Equals(i.Initials, initials, StringComparison.OrdinalIgnoreCase) &&
            i.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Instructor instructor) { }

    /// <inheritdoc/>
    public void Update(Instructor instructor, DbTransaction? tx = null) { }

    /// <inheritdoc/>
    public void Delete(string id) { }
}
