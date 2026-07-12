using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="ProgramWatch"/> entities — monitored groups of
/// courses checked for time overlaps within a semester.
/// </summary>
public interface IProgramWatchRepository
{
    /// <summary>Returns all watches for the given semester.</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    List<ProgramWatch> GetAll(string semesterId);

    /// <summary>
    /// Persists a watch, inserting a new row or updating an existing one by ID.
    /// </summary>
    /// <param name="semesterId">The semester the watch belongs to.</param>
    /// <param name="watch">The watch to save.</param>
    void Save(string semesterId, ProgramWatch watch);

    /// <summary>Removes a watch by ID.</summary>
    /// <param name="id">The watch ID to delete.</param>
    void Delete(string id);
}
