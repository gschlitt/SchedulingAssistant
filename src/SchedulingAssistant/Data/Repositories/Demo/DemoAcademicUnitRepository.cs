using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IAcademicUnitRepository"/>.
/// Returns a single hard-coded demo university so that
/// <see cref="Services.AcademicUnitService.GetUnit"/> never throws.
/// Write operations are no-ops.
/// </summary>
public class DemoAcademicUnitRepository : IAcademicUnitRepository
{
    /// <summary>The single academic unit shown in the demo.</summary>
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
    public void Insert(AcademicUnit unit) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(AcademicUnit unit) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id) { /* no-op in demo */ }
}
