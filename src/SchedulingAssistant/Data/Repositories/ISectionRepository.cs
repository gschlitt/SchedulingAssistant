using System.Data.Common;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Section"/> entities (the core scheduling entity).
/// </summary>
public interface ISectionRepository
{
    /// <summary>Returns all sections across all semesters.</summary>
    List<Section> GetAll();

    /// <summary>Returns all sections belonging to the given semester, ordered by section code.</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    List<Section> GetAll(string semesterId);

    /// <summary>Returns the section with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Section? GetById(string id);

    /// <summary>Returns all sections assigned to the given course.</summary>
    List<Section> GetByCourseId(string courseId);

    /// <summary>
    /// Inserts a new section. The <see cref="Section.Id"/> must already be set.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Insert(Section section, DbTransaction? tx = null);

    /// <summary>
    /// Updates the section matched by <see cref="Section.Id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Update(Section section, DbTransaction? tx = null);

    /// <summary>Returns the total number of sections belonging to the given academic year.</summary>
    int CountByAcademicYear(string academicYearId);

    /// <summary>
    /// Returns <c>true</c> if a section with the same course and section code already exists in the semester.
    /// Pass <paramref name="excludeId"/> to allow the current section to match without triggering a duplicate.
    /// </summary>
    bool ExistsBySectionCode(string semesterId, string courseId, string sectionCode, string? excludeId);

    /// <summary>Deletes the section with the given <paramref name="id"/>.</summary>
    void Delete(string id);

    /// <summary>Returns the number of sections in the given semester.</summary>
    int CountBySemesterId(string semesterId);

    /// <summary>Deletes all sections belonging to the given semester.</summary>
    void DeleteBySemesterId(string semesterId);
}
