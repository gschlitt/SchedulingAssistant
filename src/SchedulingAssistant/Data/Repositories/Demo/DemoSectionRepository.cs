using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISectionRepository"/> backed by
/// a mutable copy of <see cref="DemoData.Sections"/>. Write operations mutate
/// the in-memory list so edits are reflected within the browser session.
/// Changes are lost on page reload.
/// </summary>
public class DemoSectionRepository : ISectionRepository
{
    private readonly List<Section> _sections = [.. DemoData.Sections];

    /// <inheritdoc/>
    public List<Section> GetAll() => [.. _sections];

    /// <inheritdoc/>
    public List<Section> GetAll(string semesterId) =>
        [.. _sections
            .Where(s => s.SemesterId == semesterId)
            .OrderBy(s => s.SectionCode)];

    /// <inheritdoc/>
    public Section? GetById(string id) =>
        _sections.FirstOrDefault(s => s.Id == id);

    /// <inheritdoc/>
    public List<Section> GetByCourseId(string courseId) =>
        [.. _sections.Where(s => s.CourseId == courseId)];

    /// <inheritdoc/>
    public bool ExistsBySectionCode(string semesterId, string courseId, string sectionCode, string? excludeId) =>
        _sections.Any(s =>
            s.SemesterId == semesterId &&
            s.CourseId == courseId &&
            string.Equals(s.SectionCode, sectionCode, StringComparison.OrdinalIgnoreCase) &&
            s.Id != excludeId);

    /// <inheritdoc/>
    public int CountByAcademicYear(string academicYearId)
    {
        var semesterIds = DemoData.Semesters
            .Where(s => s.AcademicYearId == academicYearId)
            .Select(s => s.Id)
            .ToHashSet();
        return _sections.Count(s => semesterIds.Contains(s.SemesterId));
    }

    /// <inheritdoc/>
    public int CountBySemesterId(string semesterId) =>
        _sections.Count(s => s.SemesterId == semesterId);

    /// <inheritdoc/>
    public void Insert(Section section, DbTransaction? tx = null) =>
        _sections.Add(section);

    /// <inheritdoc/>
    public void Update(Section section, DbTransaction? tx = null)
    {
        int i = _sections.FindIndex(s => s.Id == section.Id);
        if (i >= 0) _sections[i] = section;
    }

    /// <inheritdoc/>
    public void Delete(string id) =>
        _sections.RemoveAll(s => s.Id == id);

    /// <inheritdoc/>
    public void DeleteBySemesterId(string semesterId) =>
        _sections.RemoveAll(s => s.SemesterId == semesterId);
}
