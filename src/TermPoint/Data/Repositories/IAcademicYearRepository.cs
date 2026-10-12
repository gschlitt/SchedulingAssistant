using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="AcademicYear"/> entities.
/// </summary>
public interface IAcademicYearRepository
{
    /// <summary>Returns all academic years, ordered by name.</summary>
    List<AcademicYear> GetAll();

    /// <summary>Returns the academic year with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    AcademicYear? GetById(string id);

    /// <summary>
    /// Returns <c>true</c> if an academic year with the given <paramref name="name"/> already exists.
    /// </summary>
    bool ExistsByName(string name);

    /// <summary>Inserts a new academic year. The <see cref="AcademicYear.Id"/> must already be set.</summary>
    void Insert(AcademicYear academicYear);

    /// <summary>Updates the academic year matched by <see cref="AcademicYear.Id"/>.</summary>
    void Update(AcademicYear academicYear);

    /// <summary>
    /// Deletes the academic year with the given <paramref name="id"/>. This removes only the
    /// academic-year row; its semesters and legal start times are the caller's responsibility
    /// (see <c>SemesterContentsService</c>).
    /// </summary>
    /// <param name="id">The academic year to delete.</param>
    /// <param name="tx">
    /// Optional transaction to enlist this delete in a larger atomic operation. Pass <c>null</c>
    /// (the default) for a standalone delete.
    /// </param>
    void Delete(string id, DbTransaction? tx = null);
}
