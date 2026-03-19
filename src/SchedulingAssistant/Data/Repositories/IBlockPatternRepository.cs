using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="BlockPattern"/> entities (standard meeting-time patterns).
/// </summary>
public interface IBlockPatternRepository
{
    /// <summary>Returns all block patterns, ordered by name.</summary>
    List<BlockPattern> GetAll();

    /// <summary>Returns the block pattern with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    BlockPattern? GetById(string id);

    /// <summary>Inserts a new block pattern. The <see cref="BlockPattern.Id"/> must already be set.</summary>
    void Insert(BlockPattern pattern);

    /// <summary>Updates the block pattern matched by <see cref="BlockPattern.Id"/>.</summary>
    void Update(BlockPattern pattern);

    /// <summary>Deletes the block pattern with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
