using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Release"/> entities
/// (workload release/reduction assignments for instructors within a semester).
/// </summary>
public interface IReleaseRepository
{
    /// <summary>Returns all releases for the given semester.</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    List<Release> GetBySemester(string semesterId);

    /// <summary>Returns all releases for a specific instructor within a semester.</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    /// <param name="instructorId">The instructor to filter by.</param>
    List<Release> GetByInstructor(string semesterId, string instructorId);

    /// <summary>Returns the release with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Release? GetById(string id);

    /// <summary>Inserts a new release. The <see cref="Release.Id"/> must already be set.</summary>
    void Insert(Release release);

    /// <summary>Updates the release matched by <see cref="Release.Id"/>.</summary>
    void Update(Release release);

    /// <summary>Deletes the release with the given <paramref name="id"/>.</summary>
    void Delete(string id);

    /// <summary>Returns the number of releases in the given semester.</summary>
    /// <param name="semesterId">The semester to count in.</param>
    int CountBySemesterId(string semesterId);

    /// <summary>
    /// Deletes every release belonging to the given semester (part of Empty Semester, spec item 8).
    /// </summary>
    /// <param name="semesterId">The semester whose releases are deleted.</param>
    /// <param name="tx">
    /// Optional transaction to enlist this delete in. Pass <c>null</c> (the default) for a
    /// standalone delete.
    /// </param>
    void DeleteBySemesterId(string semesterId, DbTransaction? tx = null);
}
