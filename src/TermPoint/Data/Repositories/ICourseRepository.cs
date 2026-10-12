using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Course"/> entities.
/// </summary>
public interface ICourseRepository
{
    /// <summary>Returns all courses, ordered by calendar code.</summary>
    List<Course> GetAll();

    /// <summary>Returns all courses belonging to the given subject.</summary>
    /// <param name="subjectId">The subject's ID to filter by.</param>
    List<Course> GetBySubject(string subjectId);

    /// <summary>Returns all courses whose <c>IsActive</c> flag is true.</summary>
    List<Course> GetAllActive();

    /// <summary>Returns the course with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Course? GetById(string id);

    /// <summary>
    /// Returns <c>true</c> if any section exists that references the given course.
    /// <para>
    /// Ignores orphans: sections whose semester no longer exists (left by older app versions when an
    /// academic year was deleted; see spec item 8). They are invisible to the user and must not
    /// block deleting the course. The orphans are left untouched in the database.
    /// </para>
    /// </summary>
    /// <param name="courseId">The course to check.</param>
    bool HasSections(string courseId);

    /// <summary>
    /// Returns <c>true</c> if a course with the given <paramref name="code"/> already exists.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByCalendarCode(string code, string? excludeId = null);

    /// <summary>Inserts a new course. The <see cref="Course.Id"/> must already be set.</summary>
    void Insert(Course course);

    /// <summary>
    /// Updates the course matched by <see cref="Course.Id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Update(Course course, DbTransaction? tx = null);

    /// <summary>Deletes the course with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
