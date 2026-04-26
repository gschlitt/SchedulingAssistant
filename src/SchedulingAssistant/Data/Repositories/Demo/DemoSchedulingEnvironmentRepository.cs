using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ISchedulingEnvironmentRepository"/>
/// backed by typed lists in <see cref="DemoData"/>. Write operations are no-ops.
/// </summary>
public class DemoSchedulingEnvironmentRepository : ISchedulingEnvironmentRepository
{
    /// <inheritdoc/>
    public List<SchedulingEnvironmentValue> GetAll(string type) =>
        type switch
        {
            SchedulingEnvironmentTypes.SectionType => [.. DemoData.SectionTypes.OrderBy(v => v.SortOrder)],
            SchedulingEnvironmentTypes.MeetingType => [.. DemoData.MeetingTypes.OrderBy(v => v.SortOrder)],
            SchedulingEnvironmentTypes.StaffType   => [.. DemoData.StaffTypes.OrderBy(v => v.SortOrder)],
            SchedulingEnvironmentTypes.Tag         => [.. DemoData.Tags.OrderBy(v => v.SortOrder)],
            SchedulingEnvironmentTypes.Resource    => [.. DemoData.Resources.OrderBy(v => v.SortOrder)],
            SchedulingEnvironmentTypes.Reserve     => [.. DemoData.Reserves.OrderBy(v => v.SortOrder)],
            _                                      => [],
        };

    /// <inheritdoc/>
    public SchedulingEnvironmentValue? GetById(string id) =>
        DemoData.AllSchedulingEnvironmentValues.FirstOrDefault(v => v.Id == id);

    /// <inheritdoc/>
    public void Insert(string type, SchedulingEnvironmentValue value) { }

    /// <inheritdoc/>
    public void Update(SchedulingEnvironmentValue value) { }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null) { }

    /// <inheritdoc/>
    public bool ExistsByName(string type, string name, string? excludeId = null) =>
        GetAll(type).Any(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase) &&
            v.Id != excludeId);
}
