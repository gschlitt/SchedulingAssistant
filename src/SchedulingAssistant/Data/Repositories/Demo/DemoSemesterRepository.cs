using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ISemesterRepository"/> backed by
/// <see cref="DemoData.Semesters"/>.  Write operations are no-ops.
/// </summary>
public class DemoSemesterRepository : ISemesterRepository
{
    /// <inheritdoc/>
    public List<Semester> GetAll() =>
        [.. DemoData.Semesters.OrderBy(s => s.SortOrder)];

    /// <inheritdoc/>
    public List<Semester> GetByAcademicYear(string academicYearId) =>
        [.. DemoData.Semesters
            .Where(s => s.AcademicYearId == academicYearId)
            .OrderBy(s => s.SortOrder)];

    /// <inheritdoc/>
    public Semester? GetById(string id) =>
        DemoData.Semesters.FirstOrDefault(s => s.Id == id);

    /// <inheritdoc/>
    public void Insert(Semester semester) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(Semester semester) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void DeleteByAcademicYear(string academicYearId) { /* no-op in demo */ }
}
