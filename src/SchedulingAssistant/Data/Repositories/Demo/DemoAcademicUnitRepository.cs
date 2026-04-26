using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IAcademicUnitRepository"/>.
/// Returns a single hard-coded demo university. Write operations are no-ops.
/// </summary>
public class DemoAcademicUnitRepository : IAcademicUnitRepository
{
    private static readonly AcademicUnit DemoUnit = new()
    {
        Id   = "demo-academic-unit",
        Name = "Demo University"
    };

    /// <inheritdoc/>
    public List<AcademicUnit> GetAll() => [DemoUnit];

    /// <inheritdoc/>
    public AcademicUnit? GetById(string id) =>
        id == DemoUnit.Id ? DemoUnit : null;

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null) => false;

    /// <inheritdoc/>
    public void Insert(AcademicUnit unit) { }

    /// <inheritdoc/>
    public void Update(AcademicUnit unit) { }

    /// <inheritdoc/>
    public void Delete(string id) { }
}
