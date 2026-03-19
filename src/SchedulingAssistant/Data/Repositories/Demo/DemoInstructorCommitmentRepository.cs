using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IInstructorCommitmentRepository"/>.
/// No commitment data is included in the demo snapshot, so all query methods
/// return empty/null results.  Write operations are no-ops.
/// </summary>
public class DemoInstructorCommitmentRepository : IInstructorCommitmentRepository
{
    /// <inheritdoc/>
    public List<InstructorCommitment> GetByInstructor(string semesterId, string instructorId) => [];

    /// <inheritdoc/>
    public InstructorCommitment? GetById(string id) => null;

    /// <inheritdoc/>
    public void Insert(InstructorCommitment commitment) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(InstructorCommitment commitment) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id) { /* no-op in demo */ }
}
