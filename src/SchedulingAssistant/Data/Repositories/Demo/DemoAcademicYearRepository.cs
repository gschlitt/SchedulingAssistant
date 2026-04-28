using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IAcademicYearRepository"/>
/// seeded from <see cref="DemoData.AcademicYear"/>. Changes are lost on page reload.
/// </summary>
public class DemoAcademicYearRepository : IAcademicYearRepository
{
    private readonly List<AcademicYear> _years = [DemoData.AcademicYear];

    /// <inheritdoc/>
    public List<AcademicYear> GetAll() => [.. _years];

    /// <inheritdoc/>
    public AcademicYear? GetById(string id) =>
        _years.FirstOrDefault(y => y.Id == id);

    /// <inheritdoc/>
    public bool ExistsByName(string name) =>
        _years.Any(y => string.Equals(y.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public void Insert(AcademicYear academicYear) => _years.Add(academicYear);

    /// <inheritdoc/>
    public void Update(AcademicYear academicYear)
    {
        int i = _years.FindIndex(y => y.Id == academicYear.Id);
        if (i >= 0) _years[i] = academicYear;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _years.RemoveAll(y => y.Id == id);
}
