using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IInstructorCommitmentRepository"/>.
/// No commitment data in demo; all queries return empty. Write operations are no-ops.
/// </summary>
public class DemoInstructorCommitmentRepository : IInstructorCommitmentRepository
{
    /// <inheritdoc/>
    public List<InstructorCommitment> GetByInstructor(string semesterId, string instructorId) => [];

    /// <inheritdoc/>
    public InstructorCommitment? GetById(string id) => null;

    /// <inheritdoc/>
    public void Insert(InstructorCommitment commitment) { }

    /// <inheritdoc/>
    public void Update(InstructorCommitment commitment) { }

    /// <inheritdoc/>
    public void Delete(string id) { }
}
