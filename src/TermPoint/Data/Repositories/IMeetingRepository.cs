using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Meeting"/> entities.
/// </summary>
public interface IMeetingRepository
{
    /// <summary>Returns all meetings belonging to the given semester, ordered by title.</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    List<Meeting> GetAll(string semesterId);

    /// <summary>Returns the meeting with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Meeting? GetById(string id);

    /// <summary>Inserts a new meeting. The <see cref="Meeting.Id"/> must already be set.</summary>
    void Insert(Meeting meeting);

    /// <summary>Updates the meeting matched by <see cref="Meeting.Id"/>.</summary>
    void Update(Meeting meeting);

    /// <summary>Deletes the meeting with the given <paramref name="id"/>.</summary>
    void Delete(string id);

    /// <summary>Deletes all meetings belonging to the given semester.</summary>
    /// <param name="semesterId">The semester whose meetings are deleted.</param>
    /// <param name="tx">
    /// Optional transaction to enlist this delete in a larger atomic operation (Empty Semester,
    /// spec item 8). Pass <c>null</c> (the default) for a standalone delete.
    /// </param>
    void DeleteBySemesterId(string semesterId, DbTransaction? tx = null);

    /// <summary>Returns the number of meetings in the given semester.</summary>
    int CountBySemesterId(string semesterId);
}
