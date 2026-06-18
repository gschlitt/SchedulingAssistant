using System.Data.Common;
using TermPoint.Demo;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Data.Repositories.Demo;

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
    private readonly List<SchedulingEnvironmentValue> _roomTypes     = [.. DemoData.RoomTypes];

    /// <summary>All buckets, used by the type-agnostic operations (lookup, update, delete).</summary>
    private List<SchedulingEnvironmentValue>[] AllBuckets =>
        [_sectionTypes, _meetingTypes, _staffTypes, _tags, _resources, _reserves, _roomTypes];

    private List<SchedulingEnvironmentValue> ListFor(string type) => type switch
    {
        SchedulingEnvironmentTypes.SectionType => _sectionTypes,
        SchedulingEnvironmentTypes.MeetingType => _meetingTypes,
        SchedulingEnvironmentTypes.StaffType   => _staffTypes,
        SchedulingEnvironmentTypes.Tag         => _tags,
        SchedulingEnvironmentTypes.Resource    => _resources,
        SchedulingEnvironmentTypes.Reserve     => _reserves,
        SchedulingEnvironmentTypes.RoomType    => _roomTypes,
        _                                      => []
    };

    private IEnumerable<SchedulingEnvironmentValue> AllValues =>
        AllBuckets.SelectMany(b => b);

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
        foreach (var bucket in AllBuckets) // find which list owns this id
        {
            int i = bucket.FindIndex(v => v.Id == value.Id);
            if (i >= 0) { bucket[i] = value; return; }
        }
    }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null)
    {
        foreach (var bucket in AllBuckets)
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
