using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IInstructorCommitmentRepository"/>.
/// Starts empty; all CRUD operations update the in-memory list. Changes are lost on
/// page reload.
/// </summary>
public class DemoInstructorCommitmentRepository : IInstructorCommitmentRepository
{
    private readonly List<InstructorCommitment> _commitments = [];

    /// <inheritdoc/>
    public List<InstructorCommitment> GetByInstructor(string semesterId, string instructorId) =>
        [.. _commitments.Where(c => c.SemesterId == semesterId && c.InstructorId == instructorId)];

    /// <inheritdoc/>
    public InstructorCommitment? GetById(string id) =>
        _commitments.FirstOrDefault(c => c.Id == id);

    /// <inheritdoc/>
    public void Insert(InstructorCommitment commitment) => _commitments.Add(commitment);

    /// <inheritdoc/>
    public void Update(InstructorCommitment commitment)
    {
        int i = _commitments.FindIndex(c => c.Id == commitment.Id);
        if (i >= 0) _commitments[i] = commitment;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _commitments.RemoveAll(c => c.Id == id);
}
