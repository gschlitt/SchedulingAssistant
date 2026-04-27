using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISemesterRepository"/> backed by a
/// mutable copy of <see cref="DemoData.Semesters"/>. All CRUD operations update the
/// in-memory list; changes are lost on page reload.
/// </summary>
public class DemoSemesterRepository : ISemesterRepository
{
    private readonly List<Semester> _semesters = [.. DemoData.Semesters];

    /// <inheritdoc/>
    public List<Semester> GetAll() =>
        [.. _semesters.OrderBy(s => s.SortOrder)];

    /// <inheritdoc/>
    public List<Semester> GetByAcademicYear(string academicYearId) =>
        [.. _semesters
            .Where(s => s.AcademicYearId == academicYearId)
            .OrderBy(s => s.SortOrder)];

    /// <inheritdoc/>
    public Semester? GetById(string id) =>
        _semesters.FirstOrDefault(s => s.Id == id);

    /// <inheritdoc/>
    public void Insert(Semester semester) => _semesters.Add(semester);

    /// <inheritdoc/>
    public void Update(Semester semester)
    {
        int i = _semesters.FindIndex(s => s.Id == semester.Id);
        if (i >= 0) _semesters[i] = semester;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _semesters.RemoveAll(s => s.Id == id);

    /// <inheritdoc/>
    public void DeleteByAcademicYear(string academicYearId) =>
        _semesters.RemoveAll(s => s.AcademicYearId == academicYearId);
}
