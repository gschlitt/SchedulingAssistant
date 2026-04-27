using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISchedulingEnvironmentRepository"/>
/// backed by mutable copies of the typed lists in <see cref="DemoData"/>. All CRUD
/// operations update the in-memory lists; changes are lost on page reload.
/// </summary>
public class DemoSchedulingEnvironmentRepository : ISchedulingEnvironmentRepository
{
    private readonly List<SchedulingEnvironmentValue> _sectionTypes  = [.. DemoData.SectionTypes];
    private readonly List<SchedulingEnvironmentValue> _meetingTypes  = [.. DemoData.MeetingTypes];
    private readonly List<SchedulingEnvironmentValue> _staffTypes    = [.. DemoData.StaffTypes];
    private readonly List<SchedulingEnvironmentValue> _tags          = [.. DemoData.Tags];
    private readonly List<SchedulingEnvironmentValue> _resources     = [.. DemoData.Resources];
    private readonly List<SchedulingEnvironmentValue> _reserves      = [.. DemoData.Reserves];

    private List<SchedulingEnvironmentValue> ListFor(string type) => type switch
    {
        SchedulingEnvironmentTypes.SectionType => _sectionTypes,
        SchedulingEnvironmentTypes.MeetingType => _meetingTypes,
        SchedulingEnvironmentTypes.StaffType   => _staffTypes,
        SchedulingEnvironmentTypes.Tag         => _tags,
        SchedulingEnvironmentTypes.Resource    => _resources,
        SchedulingEnvironmentTypes.Reserve     => _reserves,
        _                                      => []
    };

    private IEnumerable<SchedulingEnvironmentValue> AllValues =>
        _sectionTypes
            .Concat(_meetingTypes)
            .Concat(_staffTypes)
            .Concat(_tags)
            .Concat(_resources)
            .Concat(_reserves);

    /// <inheritdoc/>
    public List<SchedulingEnvironmentValue> GetAll(string type) =>
        [.. ListFor(type).OrderBy(v => v.SortOrder)];

    /// <inheritdoc/>
    public SchedulingEnvironmentValue? GetById(string id) =>
        AllValues.FirstOrDefault(v => v.Id == id);

    /// <inheritdoc/>
    public void Insert(string type, SchedulingEnvironmentValue value) =>
        ListFor(type).Add(value);

    /// <inheritdoc/>
    public void Update(SchedulingEnvironmentValue value)
    {
        var list = AllValues.ToList(); // find which list owns this id
        foreach (var bucket in new[] { _sectionTypes, _meetingTypes, _staffTypes, _tags, _resources, _reserves })
        {
            int i = bucket.FindIndex(v => v.Id == value.Id);
            if (i >= 0) { bucket[i] = value; return; }
        }
    }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null)
    {
        foreach (var bucket in new[] { _sectionTypes, _meetingTypes, _staffTypes, _tags, _resources, _reserves })
        {
            if (bucket.RemoveAll(v => v.Id == id) > 0) return;
        }
    }

    /// <inheritdoc/>
    public bool ExistsByName(string type, string name, string? excludeId = null) =>
        ListFor(type).Any(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase) &&
            v.Id != excludeId);
}
