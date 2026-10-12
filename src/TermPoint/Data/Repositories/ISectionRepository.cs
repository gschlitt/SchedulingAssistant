using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Section"/> entities (the core scheduling entity).
/// </summary>
public interface ISectionRepository
{
    /// <summary>
    /// Returns all sections across all semesters.
    /// <para>
    /// Excludes orphans: sections whose semester no longer exists (left by older app versions when
    /// an academic year was deleted; see spec item 8). They cannot be shown in any semester, so
    /// whole-database sweeps must not see them. The orphans are left untouched in the database.
    /// </para>
    /// </summary>
    List<Section> GetAll();

    /// <summary>Returns all sections belonging to the given semester, ordered by section code.</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    List<Section> GetAll(string semesterId);

    /// <summary>Returns the section with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Section? GetById(string id);

    /// <summary>
    /// Returns all sections assigned to the given course, across all semesters.
    /// <para>
    /// Excludes orphans: sections whose semester no longer exists (left by older app versions when
    /// an academic year was deleted; see spec item 8), which would otherwise appear as ghost rows in
    /// Course History. The orphans are left untouched in the database.
    /// </para>
    /// </summary>
    /// <param name="courseId">The course whose sections are returned.</param>
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

    /// <summary>
    /// Returns <c>true</c> if a section with the same course and section code already exists in the semester.
    /// Pass <paramref name="excludeId"/> to allow the current section to match without triggering a duplicate.
    /// </summary>
    bool ExistsBySectionCode(string semesterId, string courseId, string sectionCode, string? excludeId);

    /// <summary>
    /// Deletes the section with the given <paramref name="id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this delete in a larger
    /// transaction, consistent with <see cref="Insert"/> and <see cref="Update"/>. (F5.)
    /// </summary>
    void Delete(string id, DbTransaction? tx = null);

    /// <summary>Returns the number of sections in the given semester.</summary>
    int CountBySemesterId(string semesterId);

    /// <summary>Deletes all sections belonging to the given semester.</summary>
    /// <param name="semesterId">The semester whose sections are deleted.</param>
    /// <param name="tx">
    /// Optional transaction to enlist this delete in a larger atomic operation (Empty Semester,
    /// spec item 8). Pass <c>null</c> (the default) for a standalone delete.
    /// </param>
    void DeleteBySemesterId(string semesterId, DbTransaction? tx = null);
}
