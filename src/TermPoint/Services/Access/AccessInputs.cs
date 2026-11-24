using System.Collections.Concurrent;
using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// Everything the access engine reads about one semester (Access Analyzer rules 28-30): the semester's saved
/// sections with a <see cref="Proposal"/> laid over them, the courses, the section types in sort order, whether
/// the access setup has been done, and the suggestion source for courses whose components were never set. Built
/// once per question and never changed; an empty proposal is the saved state, so there is no second code path.
/// Pure data and lookups: no database, no DI.
/// </summary>
public sealed class AccessInputs
{
    private readonly SemesterData _data;
    private readonly Dictionary<string, SchedulingEnvironmentValue> _typeById;
    private readonly Dictionary<string, int> _typeOrder;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _componentsByCourse = new();

    /// <summary>The semester's view: saved sections with the proposal laid over them.</summary>
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>The view's sections by course id, each list in stable order (section code). Sections with no course are left out.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Section>> SectionsByCourse { get; }

    /// <summary>The proposal laid over the saved sections (<see cref="Proposal.Empty"/> for the saved state).</summary>
    public Proposal Proposal { get; }

    /// <summary>Whether the access setup (the wizard) has been done: the stored answers are used instead of the suggestion.</summary>
    public bool SetUp { get; }

    /// <summary>The section types in sort order.</summary>
    public IReadOnlyList<SchedulingEnvironmentValue> SectionTypes => _data.SortedTypes;

    /// <summary>Orders course ids by calendar code (ignoring case), then id: the stable order of Q34.</summary>
    public IComparer<string> CourseOrder { get; }

    /// <summary>Creates the inputs for one semester.</summary>
    /// <param name="savedSections">The semester's saved sections.</param>
    /// <param name="coursesById">Every course, by id. A section whose course is missing is treated as a course never set up.</param>
    /// <param name="sectionTypes">The section types (re-sorted by <see cref="SchedulingEnvironmentValue.SortOrder"/>, stably).</param>
    /// <param name="setUp">Whether the access setup has been done.</param>
    /// <param name="sectionTypeIdsByCourse">
    /// The suggestion source: for each course id, the section type ids its sections have in every semester
    /// (<c>ISectionRepository.GetSectionTypeIdsByCourse</c>).
    /// </param>
    /// <param name="proposal">The what-if to lay over the saved sections; <c>null</c> means none.</param>
    public AccessInputs(
        IEnumerable<Section> savedSections,
        IReadOnlyDictionary<string, Course> coursesById,
        IEnumerable<SchedulingEnvironmentValue> sectionTypes,
        bool setUp,
        IReadOnlyDictionary<string, IReadOnlySet<string>> sectionTypeIdsByCourse,
        Proposal? proposal = null)
        : this(new SemesterData(savedSections.ToList(), coursesById, sectionTypes.OrderBy(t => t.SortOrder).ToList(),
                                setUp, sectionTypeIdsByCourse),
               proposal ?? Proposal.Empty)
    {
    }

    /// <summary>The parts of the inputs that do not change with the proposal, shared by <see cref="With"/>.</summary>
    private sealed record SemesterData(
        IReadOnlyList<Section> Saved,
        IReadOnlyDictionary<string, Course> CoursesById,
        IReadOnlyList<SchedulingEnvironmentValue> SortedTypes,
        bool SetUp,
        IReadOnlyDictionary<string, IReadOnlySet<string>> SuggestionSource);

    /// <summary>Shared constructor: builds the view and the lookups from the shared data and a proposal.</summary>
    private AccessInputs(SemesterData data, Proposal proposal)
    {
        _data = data;
        var sortedTypes = data.SortedTypes;
        SetUp = data.SetUp;
        Proposal = proposal;
        _typeById = new Dictionary<string, SchedulingEnvironmentValue>();
        _typeOrder = new Dictionary<string, int>();
        for (int i = 0; i < sortedTypes.Count; i++)
        {
            _typeById.TryAdd(sortedTypes[i].Id, sortedTypes[i]);
            _typeOrder.TryAdd(sortedTypes[i].Id, i);
        }

        CourseOrder = Comparer<string>.Create((a, b) =>
        {
            int c = string.Compare(CourseCode(a), CourseCode(b), StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });

        Sections = ApplyProposal(data.Saved, proposal);
        SectionsByCourse = Sections
            .Where(s => s.CourseId is not null)
            .GroupBy(s => s.CourseId!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Section>)g.OrderBy(s => s, SectionOrder.Comparer).ToList());
    }

    /// <summary>
    /// The same semester and lookups with a different proposal. The new proposal replaces the old one; it is not
    /// laid on top of it.
    /// </summary>
    /// <param name="proposal">The what-if to lay over the saved sections.</param>
    /// <returns>New inputs; this instance is unchanged.</returns>
    public AccessInputs With(Proposal proposal) =>
        new(_data, proposal);

    /// <summary>
    /// Whether a section type is a component type: once set up, the type's stored Component flag; before that,
    /// whether its name looks like a component (<see cref="ComponentNames.LooksLikeComponent"/>). A type that does
    /// not exist (deleted) is not one.
    /// </summary>
    /// <param name="typeId">The section type id.</param>
    /// <returns><c>true</c> when the type is a component type.</returns>
    public bool IsComponentType(string? typeId)
    {
        if (typeId is null || !_typeById.TryGetValue(typeId, out var type)) return false;
        return SetUp ? type.IsComponent : ComponentNames.LooksLikeComponent(type.Name);
    }

    /// <summary>
    /// A course's components (rules 4-6), as section type ids sorted by section type order. Once set up and when
    /// the course stores a list (even an empty one), that list is used; otherwise the suggestion: the section types
    /// the course's sections have in every semester plus those in this view. Either way, only ids that are still
    /// component types count, so deleted and no-longer-component ids are ignored.
    /// </summary>
    /// <param name="courseId">The course id.</param>
    /// <returns>The component type ids; empty when students take one section.</returns>
    public IReadOnlyList<string> ComponentsOf(string courseId) =>
        _componentsByCourse.GetOrAdd(courseId, ResolveComponents);

    /// <summary>The course's calendar code (e.g. "BIOL 201"), or its id when the course is unknown.</summary>
    /// <param name="courseId">The course id.</param>
    /// <returns>The code used to name and order the course.</returns>
    public string CourseCode(string courseId) =>
        _data.CoursesById.TryGetValue(courseId, out var c) && !string.IsNullOrEmpty(c.CalendarCode) ? c.CalendarCode : courseId;

    /// <summary>Resolves a course's components (see <see cref="ComponentsOf"/>).</summary>
    private IReadOnlyList<string> ResolveComponents(string courseId)
    {
        _data.CoursesById.TryGetValue(courseId, out var course);
        IEnumerable<string> ids;
        if (SetUp && course?.ComponentTypeIds is { } stored)
        {
            ids = stored;
        }
        else
        {
            var seen = _data.SuggestionSource.TryGetValue(courseId, out var s) ? s : (IEnumerable<string>)[];
            var inView = SectionsByCourse.TryGetValue(courseId, out var mine)
                ? mine.Select(x => x.SectionTypeId).OfType<string>()
                : [];
            ids = seen.Concat(inView);
        }
        return ids.Distinct().Where(IsComponentType).OrderBy(id => _typeOrder[id]).ToList();
    }

    /// <summary>Lays a proposal over saved sections: replaced in place, new ones appended, removed ones dropped.</summary>
    private static IReadOnlyList<Section> ApplyProposal(IReadOnlyList<Section> saved, Proposal proposal)
    {
        if (proposal.IsEmpty) return saved;
        var upserts = proposal.UpsertedSections.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Last());
        var view = new List<Section>();
        var placed = new HashSet<string>();
        foreach (var s in saved)
        {
            if (proposal.RemovedSectionIds.Contains(s.Id)) continue;
            if (upserts.TryGetValue(s.Id, out var replacement)) { view.Add(replacement); placed.Add(s.Id); }
            else view.Add(s);
        }
        foreach (var (id, s) in upserts)
            if (!placed.Contains(id) && !proposal.RemovedSectionIds.Contains(id)) view.Add(s);
        return view;
    }
}

/// <summary>The stable section order (Q34): section code (ignoring case), then id.</summary>
internal static class SectionOrder
{
    /// <summary>Compares sections by code, then id.</summary>
    internal static readonly IComparer<Section> Comparer = Comparer<Section>.Create((a, b) =>
    {
        int c = string.Compare(a.SectionCode, b.SectionCode, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    });
}
