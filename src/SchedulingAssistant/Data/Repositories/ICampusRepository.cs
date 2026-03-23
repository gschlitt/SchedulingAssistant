using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Campus"/> entities.
/// </summary>
public interface ICampusRepository
{
    /// <summary>Returns all campuses ordered by <see cref="Campus.SortOrder"/>.</summary>
    List<Campus> GetAll();

    /// <summary>Returns the campus with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Campus? GetById(string id);

    /// <summary>
    /// Returns <c>true</c> when a campus with the given <paramref name="name"/> already exists
    /// (case-insensitive). Pass <paramref name="excludeId"/> to exempt the record being edited.
    /// </summary>
    bool ExistsByName(string name, string? excludeId = null);

    /// <summary>Inserts a new campus. <see cref="Campus.Id"/> must already be set.</summary>
    void Insert(Campus campus);

    /// <summary>Updates the campus matched by <see cref="Campus.Id"/>.</summary>
    void Update(Campus campus);

    /// <summary>Deletes the campus with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
