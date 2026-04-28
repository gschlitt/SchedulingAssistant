using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISectionPrefixRepository"/>
/// seeded from <see cref="DemoData.SectionPrefixes"/>. Changes are lost on page reload.
/// </summary>
public class DemoSectionPrefixRepository : ISectionPrefixRepository
{
    private readonly List<SectionPrefix> _prefixes = [.. DemoData.SectionPrefixes];

    /// <inheritdoc/>
    public List<SectionPrefix> GetAll() =>
        [.. _prefixes.OrderBy(p => p.Prefix)];

    /// <inheritdoc/>
    public bool ExistsByPrefix(string prefixText, string? excludeId = null) =>
        _prefixes.Any(p =>
            string.Equals(p.Prefix, prefixText, StringComparison.OrdinalIgnoreCase) &&
            p.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(SectionPrefix prefix) => _prefixes.Add(prefix);

    /// <inheritdoc/>
    public void Update(SectionPrefix prefix)
    {
        int i = _prefixes.FindIndex(p => p.Id == prefix.Id);
        if (i >= 0) _prefixes[i] = prefix;
    }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null) =>
        _prefixes.RemoveAll(p => p.Id == id);
}
