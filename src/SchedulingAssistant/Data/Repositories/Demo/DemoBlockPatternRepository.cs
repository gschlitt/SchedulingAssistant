using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IBlockPatternRepository"/>
/// seeded from <see cref="DemoData.BlockPatterns"/>. Changes are lost on page reload.
/// </summary>
public class DemoBlockPatternRepository : IBlockPatternRepository
{
    private readonly List<BlockPattern> _patterns = [.. DemoData.BlockPatterns];

    /// <inheritdoc/>
    public List<BlockPattern> GetAll() => [.. _patterns];

    /// <inheritdoc/>
    public BlockPattern? GetById(string id) =>
        _patterns.FirstOrDefault(p => p.Id == id);

    /// <inheritdoc/>
    public void Insert(BlockPattern pattern) => _patterns.Add(pattern);

    /// <inheritdoc/>
    public void Update(BlockPattern pattern)
    {
        int i = _patterns.FindIndex(p => p.Id == pattern.Id);
        if (i >= 0) _patterns[i] = pattern;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _patterns.RemoveAll(p => p.Id == id);
}
