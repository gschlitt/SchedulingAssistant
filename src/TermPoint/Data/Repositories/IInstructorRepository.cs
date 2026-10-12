using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Instructor"/> entities.
/// </summary>
public interface IInstructorRepository
{
    /// <summary>Returns all instructors, ordered by last name then first name.</summary>
    List<Instructor> GetAll();

    /// <summary>Returns only active instructors (<see cref="Instructor.IsActive"/> == true),
    /// ordered by the current <see cref="Services.AppSettings.InstructorSortMode"/>.</summary>
    List<Instructor> GetAllActive();

    /// <summary>Returns the instructor with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Instructor? GetById(string id);

    /// <summary>
    /// Returns <c>true</c> if any section exists that is assigned to the given instructor.
    /// <para>
    /// Ignores orphans: sections whose semester no longer exists (left by older app versions when an
    /// academic year was deleted; see spec item 8). They are invisible to the user and must not
    /// block deleting the instructor. The orphans are left untouched in the database.
    /// </para>
    /// </summary>
    /// <param name="instructorId">The instructor to check.</param>
    bool HasSections(string instructorId);

    /// <summary>
    /// Returns <c>true</c> if an instructor with the given <paramref name="initials"/> already exists.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByInitials(string initials, string? excludeId = null);

    /// <summary>Inserts a new instructor. The <see cref="Instructor.Id"/> must already be set.</summary>
    void Insert(Instructor instructor);

    /// <summary>
    /// Updates the instructor matched by <see cref="Instructor.Id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Update(Instructor instructor, DbTransaction? tx = null);

    /// <summary>Deletes the instructor with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
