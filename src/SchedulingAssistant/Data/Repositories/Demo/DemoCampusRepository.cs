using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ICampusRepository"/>
/// backed by <see cref="DemoData.Campuses"/>. Write operations are no-ops.
/// </summary>
public class DemoCampusRepository : ICampusRepository
{
    /// <inheritdoc/>
    public List<Campus> GetAll() =>
        [.. DemoData.Campuses.OrderBy(c => c.SortOrder)];

    /// <inheritdoc/>
    public Campus? GetById(string id) =>
        DemoData.Campuses.FirstOrDefault(c => c.Id == id);

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) =>
        DemoData.Campuses.Any(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
            c.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Campus campus) { }

    /// <inheritdoc/>
    public void Update(Campus campus) { }

    /// <inheritdoc/>
    public void Delete(string id) { }
}
