using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="AcademicUnit"/> entities (colleges, departments, etc.).
/// </summary>
public interface IAcademicUnitRepository
{
    /// <summary>Returns all academic units, unordered.</summary>
    List<AcademicUnit> GetAll();

    /// <summary>Returns the academic unit with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    AcademicUnit? GetById(string id);

    /// <summary>
    /// Returns <c>true</c> if an academic unit with the given <paramref name="name"/> already exists.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByName(string name, string? excludeId = null);

    /// <summary>Inserts a new academic unit. The <see cref="AcademicUnit.Id"/> must already be set.</summary>
    void Insert(AcademicUnit unit);

    /// <summary>Updates the academic unit matched by <see cref="AcademicUnit.Id"/>.</summary>
    void Update(AcademicUnit unit);

    /// <summary>Deletes the academic unit with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
