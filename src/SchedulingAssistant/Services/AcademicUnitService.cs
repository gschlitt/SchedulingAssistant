using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Service for accessing the single Academic Unit in the system.
/// There is always exactly one Academic Unit (created at database initialization if missing).
/// </summary>
public class AcademicUnitService(AcademicUnitRepository repository)
{
    /// <summary>
    /// Gets the one and only Academic Unit.
    /// This should never be null because one is created at database initialization.
    /// </summary>
    public AcademicUnit GetUnit()
    {
        var all = repository.GetAll();
        return all.FirstOrDefault() ?? throw new InvalidOperationException("No Academic Unit found in database.");
    }

    /// <summary>
    /// Updates the Academic Unit name.
    /// </summary>
    public void UpdateName(string newName)
    {
        var unit = GetUnit();
        unit.Name = newName.Trim();
        repository.Update(unit);
    }
}
