using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

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
        [.. SortByCurrentMode(_instructors)];

    /// <inheritdoc/>
    public List<Instructor> GetAllActive() =>
        [.. SortByCurrentMode(_instructors.Where(i => i.IsActive))];

    /// <summary>
    /// Sorts instructors according to <see cref="AppSettings.InstructorSortMode"/>,
    /// mirroring the SQL ORDER BY in <see cref="InstructorRepository.GetAll"/>.
    /// StaffType falls back to last-name order here; the caller re-sorts in memory
    /// after resolving display names.
    /// </summary>
    private static IOrderedEnumerable<Instructor> SortByCurrentMode(IEnumerable<Instructor> instructors)
    {
        return AppSettings.Current.InstructorSortMode switch
        {
            InstructorSortMode.FirstName => instructors
                .OrderBy(i => i.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.LastName, StringComparer.OrdinalIgnoreCase),
            InstructorSortMode.Initials => instructors
                .OrderBy(i => i.Initials, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.FirstName, StringComparer.OrdinalIgnoreCase),
            _ => instructors
                .OrderBy(i => i.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.FirstName, StringComparer.OrdinalIgnoreCase),
        };
    }

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
