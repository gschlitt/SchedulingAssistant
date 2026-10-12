using System.Data.Common;
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

    /// <summary>Returns the number of watches in the given semester.</summary>
    /// <param name="semesterId">The semester to count in.</param>
    int CountBySemesterId(string semesterId);

    /// <summary>
    /// Deletes every watch belonging to the given semester (part of Empty Semester, spec item 8).
    /// </summary>
    /// <param name="semesterId">The semester whose watches are deleted.</param>
    /// <param name="tx">
    /// Optional transaction to enlist this delete in. Pass <c>null</c> (the default) for a
    /// standalone delete.
    /// </param>
    void DeleteBySemesterId(string semesterId, DbTransaction? tx = null);
}
