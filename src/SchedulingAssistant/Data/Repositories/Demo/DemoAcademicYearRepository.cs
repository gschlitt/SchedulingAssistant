using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IAcademicYearRepository"/>
/// backed by <see cref="DemoData"/>. Write operations are no-ops.
/// </summary>
public class DemoAcademicYearRepository : IAcademicYearRepository
{
    /// <inheritdoc/>
    public List<AcademicYear> GetAll() => [DemoData.AcademicYear];

    /// <inheritdoc/>
    public AcademicYear? GetById(string id) =>
        DemoData.AcademicYear.Id == id ? DemoData.AcademicYear : null;

    /// <inheritdoc/>
    public bool ExistsByName(string name) =>
        string.Equals(DemoData.AcademicYear.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public void Insert(AcademicYear academicYear) { }

    /// <inheritdoc/>
    public void Update(AcademicYear academicYear) { }

    /// <inheritdoc/>
    public void Delete(string id) { }
}
