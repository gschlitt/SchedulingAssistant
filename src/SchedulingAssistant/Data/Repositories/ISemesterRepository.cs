using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Semester"/> entities.
/// </summary>
public interface ISemesterRepository
{
    /// <summary>Returns all semesters across all academic years, ordered by sort order.</summary>
    List<Semester> GetAll();

    /// <summary>Returns all semesters belonging to the given academic year, ordered by sort order.</summary>
    /// <param name="academicYearId">The academic year to filter by.</param>
    List<Semester> GetByAcademicYear(string academicYearId);

    /// <summary>Returns the semester with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Semester? GetById(string id);

    /// <summary>Inserts a new semester. The <see cref="Semester.Id"/> must already be set.</summary>
    void Insert(Semester semester);

    /// <summary>Updates the semester matched by <see cref="Semester.Id"/>.</summary>
    void Update(Semester semester);

    /// <summary>Deletes the semester with the given <paramref name="id"/>.</summary>
    void Delete(string id);

    /// <summary>Deletes all semesters belonging to the given academic year.</summary>
    void DeleteByAcademicYear(string academicYearId);
}
