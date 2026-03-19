using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ISectionPropertyRepository"/> backed by
/// the typed lists in <see cref="DemoData"/>.  Write operations are no-ops.
/// </summary>
public class DemoSectionPropertyRepository : ISectionPropertyRepository
{
    /// <summary>
    /// Returns all property values of the given <paramref name="type"/>, ordered by sort order.
    /// Recognises the canonical type strings from <see cref="SectionPropertyTypes"/>.
    /// Returns an empty list for any unrecognised type.
    /// </summary>
    public List<SectionPropertyValue> GetAll(string type) =>
        type switch
        {
            SectionPropertyTypes.SectionType => [.. DemoData.SectionTypes.OrderBy(v => v.SortOrder)],
            SectionPropertyTypes.MeetingType  => [.. DemoData.MeetingTypes.OrderBy(v => v.SortOrder)],
            SectionPropertyTypes.StaffType    => [.. DemoData.StaffTypes.OrderBy(v => v.SortOrder)],
            SectionPropertyTypes.Campus       => [.. DemoData.Campuses.OrderBy(v => v.SortOrder)],
            SectionPropertyTypes.Tag          => [.. DemoData.Tags.OrderBy(v => v.SortOrder)],
            SectionPropertyTypes.Resource     => [.. DemoData.Resources.OrderBy(v => v.SortOrder)],
            SectionPropertyTypes.Reserve      => [.. DemoData.Reserves.OrderBy(v => v.SortOrder)],
            _                                 => [],
        };

    /// <inheritdoc/>
    public SectionPropertyValue? GetById(string id) =>
        DemoData.AllSectionProperties.FirstOrDefault(v => v.Id == id);

    /// <inheritdoc/>
    public bool ExistsByName(string type, string name, string? excludeId = null) =>
        GetAll(type).Any(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase) &&
            v.Id != excludeId);

    /// <inheritdoc/>
    public void Insert(string type, SectionPropertyValue value) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(SectionPropertyValue value) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null) { /* no-op in demo */ }
}
