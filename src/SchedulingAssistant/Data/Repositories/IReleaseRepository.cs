using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

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
}
