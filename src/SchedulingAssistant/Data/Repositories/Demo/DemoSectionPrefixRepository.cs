using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ISectionPrefixRepository"/> backed by
/// <see cref="DemoData.SectionPrefixes"/>.  Write operations are no-ops.
/// </summary>
public class DemoSectionPrefixRepository : ISectionPrefixRepository
{
    /// <inheritdoc/>
    public List<SectionPrefix> GetAll() =>
        [.. DemoData.SectionPrefixes.OrderBy(p => p.Prefix)];

    /// <inheritdoc/>
    public bool ExistsByPrefix(string prefixText, string? excludeId = null) =>
        DemoData.SectionPrefixes.Any(p =>
            string.Equals(p.Prefix, prefixText, StringComparison.OrdinalIgnoreCase) &&
            p.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(SectionPrefix prefix) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(SectionPrefix prefix) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null) { /* no-op in demo */ }
}
