using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

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

    /// <summary>Deletes the academic year with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
