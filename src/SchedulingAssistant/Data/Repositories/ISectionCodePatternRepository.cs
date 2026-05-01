using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="SectionCodePattern"/> entities.
/// </summary>
public interface ISectionCodePatternRepository
{
    /// <summary>Returns all patterns ordered by <see cref="SectionCodePattern.SortOrder"/>, then name.</summary>
    List<SectionCodePattern> GetAll();

    /// <summary>Returns the pattern with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    SectionCodePattern? GetById(string id);

    /// <summary>
    /// Returns <c>true</c> when a pattern with the given name already exists.
    /// Pass <paramref name="excludeId"/> to skip the record being edited (for update validation).
    /// </summary>
    bool ExistsByName(string name, string? excludeId = null);

    /// <summary>Inserts a new pattern. The <see cref="SectionCodePattern.Id"/> must already be set.</summary>
    void Insert(SectionCodePattern pattern);

    /// <summary>Updates the pattern matched by <see cref="SectionCodePattern.Id"/>.</summary>
    void Update(SectionCodePattern pattern);

    /// <summary>Deletes the pattern with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
