using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

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
}
