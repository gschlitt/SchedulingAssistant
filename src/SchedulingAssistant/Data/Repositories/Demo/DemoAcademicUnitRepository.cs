using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IAcademicUnitRepository"/>.
/// Seeded with a single demo university; all CRUD operations update the in-memory list.
/// Changes are lost on page reload.
/// </summary>
public class DemoAcademicUnitRepository : IAcademicUnitRepository
{
    private readonly List<AcademicUnit> _units =
    [
        new() { Id = "demo-academic-unit", Name = "Demo University" }
    ];

    /// <inheritdoc/>
    public List<AcademicUnit> GetAll() => [.. _units];

    /// <inheritdoc/>
    public AcademicUnit? GetById(string id) =>
        _units.FirstOrDefault(u => u.Id == id);

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) =>
        _units.Any(u =>
            string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase) &&
            u.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(AcademicUnit unit) => _units.Add(unit);

    /// <inheritdoc/>
    public void Update(AcademicUnit unit)
    {
        int i = _units.FindIndex(u => u.Id == unit.Id);
        if (i >= 0) _units[i] = unit;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _units.RemoveAll(u => u.Id == id);
}
