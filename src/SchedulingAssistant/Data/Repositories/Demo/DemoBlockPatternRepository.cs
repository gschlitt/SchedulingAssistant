using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IBlockPatternRepository"/> backed by
/// <see cref="DemoData.BlockPatterns"/>.  Write operations are no-ops.
/// </summary>
public class DemoBlockPatternRepository : IBlockPatternRepository
{
    /// <inheritdoc/>
    public List<BlockPattern> GetAll() => [.. DemoData.BlockPatterns];

    /// <inheritdoc/>
    public BlockPattern? GetById(string id) =>
        DemoData.BlockPatterns.FirstOrDefault(p => p.Id == id);

    /// <inheritdoc/>
    public void Insert(BlockPattern pattern) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(BlockPattern pattern) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id) { /* no-op in demo */ }
}
