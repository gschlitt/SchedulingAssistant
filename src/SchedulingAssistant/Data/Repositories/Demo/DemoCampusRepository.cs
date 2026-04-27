using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ICampusRepository"/> backed by a
/// mutable copy of <see cref="DemoData.Campuses"/>. All CRUD operations update the
/// in-memory list; changes are lost on page reload.
/// </summary>
public class DemoCampusRepository : ICampusRepository
{
    private readonly List<Campus> _campuses = [.. DemoData.Campuses];

    /// <inheritdoc/>
    public List<Campus> GetAll() =>
        [.. _campuses.OrderBy(c => c.SortOrder)];

    /// <inheritdoc/>
    public Campus? GetById(string id) =>
        _campuses.FirstOrDefault(c => c.Id == id);

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) =>
        _campuses.Any(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
            c.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(Campus campus) => _campuses.Add(campus);

    /// <inheritdoc/>
    public void Update(Campus campus)
    {
        int i = _campuses.FindIndex(c => c.Id == campus.Id);
        if (i >= 0) _campuses[i] = campus;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _campuses.RemoveAll(c => c.Id == id);
}
