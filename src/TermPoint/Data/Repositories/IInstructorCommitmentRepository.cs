using System.Data.Common;
using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="InstructorCommitment"/> entities
/// (blocked-off time on an instructor's schedule for a given semester).
/// </summary>
public interface IInstructorCommitmentRepository
{
    /// <summary>
    /// Returns all commitments for a specific instructor within a semester.
    /// </summary>
    /// <param name="semesterId">The semester to filter by.</param>
    /// <param name="instructorId">The instructor to filter by.</param>
    List<InstructorCommitment> GetByInstructor(string semesterId, string instructorId);

    /// <summary>Returns the commitment with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    InstructorCommitment? GetById(string id);

    /// <summary>Inserts a new commitment. The <see cref="InstructorCommitment.Id"/> must already be set.</summary>
    void Insert(InstructorCommitment commitment);

    /// <summary>Updates the commitment matched by <see cref="InstructorCommitment.Id"/>.</summary>
    void Update(InstructorCommitment commitment);

    /// <summary>Deletes the commitment with the given <paramref name="id"/>.</summary>
    void Delete(string id);

    /// <summary>Returns the number of commitments (all instructors) in the given semester.</summary>
    /// <param name="semesterId">The semester to count in.</param>
    int CountBySemesterId(string semesterId);

    /// <summary>
    /// Deletes every commitment (all instructors) belonging to the given semester
    /// (part of Empty Semester, spec item 8).
    /// </summary>
    /// <param name="semesterId">The semester whose commitments are deleted.</param>
    /// <param name="tx">
    /// Optional transaction to enlist this delete in. Pass <c>null</c> (the default) for a
    /// standalone delete.
    /// </param>
    void DeleteBySemesterId(string semesterId, DbTransaction? tx = null);
}
