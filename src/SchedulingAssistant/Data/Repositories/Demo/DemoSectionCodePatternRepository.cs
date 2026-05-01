using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISectionCodePatternRepository"/>.
/// Starts empty; all CRUD operations update the in-memory list only.
/// Changes are lost on page reload.
/// </summary>
public class DemoSectionCodePatternRepository : ISectionCodePatternRepository
{
    private readonly List<SectionCodePattern> _patterns = [];

    /// <inheritdoc/>
    public List<SectionCodePattern> GetAll() =>
        [.. _patterns.OrderBy(p => p.SortOrder).ThenBy(p => p.Name)];

    /// <inheritdoc/>
    public SectionCodePattern? GetById(string id) =>
        _patterns.FirstOrDefault(p => p.Id == id);

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) =>
        _patterns.Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
            p.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(SectionCodePattern pattern) => _patterns.Add(pattern);

    /// <inheritdoc/>
    public void Update(SectionCodePattern pattern)
    {
        int i = _patterns.FindIndex(p => p.Id == pattern.Id);
        if (i >= 0) _patterns[i] = pattern;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _patterns.RemoveAll(p => p.Id == id);
}
