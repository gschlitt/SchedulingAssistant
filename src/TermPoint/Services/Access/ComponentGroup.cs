using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The sections that fill one component of one course (rules 7-10): a slate takes exactly one candidate from each
/// group. A course with at most one component has a single group of all its sections.
/// </summary>
public sealed class ComponentGroup
{
    /// <summary>The course id.</summary>
    public string CourseId { get; }

    /// <summary>The component's section type id; <c>null</c> for a course with at most one component (any section counts).</summary>
    public string? ComponentTypeId { get; }

    /// <summary>Every covered section that fills this component, in stable order (before pins).</summary>
    public IReadOnlyList<Section> All { get; }

    /// <summary>The sections a slate may pick from: <see cref="All"/>, or only the pinned ones when a pin narrows the group.</summary>
    public IReadOnlyList<Section> Candidates { get; }

    /// <summary>Whether a pin narrowed this group (rule 18).</summary>
    public bool Pinned { get; }

    /// <summary>Creates a group, applying pins.</summary>
    /// <param name="courseId">The course id.</param>
    /// <param name="componentTypeId">The component's section type id, or <c>null</c>.</param>
    /// <param name="all">Every section that fills the component, in stable order.</param>
    /// <param name="pins">Pinned section ids; a group holding a pinned section keeps only the pinned ones.</param>
    public ComponentGroup(string courseId, string? componentTypeId, IReadOnlyList<Section> all, IReadOnlySet<string> pins)
    {
        CourseId = courseId;
        ComponentTypeId = componentTypeId;
        All = all;
        var pinned = all.Where(s => pins.Contains(s.Id)).ToList();
        Pinned = pinned.Count > 0;
        Candidates = Pinned ? pinned : all;
    }
}

/// <summary>A component of a course that has no sections in the analysed set (rule 10): the course is left out.</summary>
/// <param name="CourseId">The course id.</param>
/// <param name="ComponentTypeId">The component's section type id.</param>
/// <param name="SectionsExistInView">
/// Whether the semester view has sections of that type for the course that the analysed set does not cover
/// (so "isn't in this watch" differs from "has none this term").
/// </param>
public sealed record MissingComponent(string CourseId, string ComponentTypeId, bool SectionsExistInView);

/// <summary>Sections of a component type their course does not list as a component (rule 8a).</summary>
/// <param name="CourseId">The course id.</param>
/// <param name="SectionTypeId">The component type the sections have.</param>
/// <param name="Sections">The course's sections of that type in the semester view.</param>
public sealed record ComponentMismatch(string CourseId, string SectionTypeId, IReadOnlyList<Section> Sections);

/// <summary>The result of grouping: the groups plus what was left out.</summary>
/// <param name="Groups">One group per component (or one per course), in stable order.</param>
/// <param name="Missing">Components with no sections; their courses have no groups.</param>
/// <param name="NotCounted">Sections of multi-component courses that fill no component (rule 8).</param>
public sealed record GroupingResult(
    IReadOnlyList<ComponentGroup> Groups,
    IReadOnlyList<MissingComponent> Missing,
    IReadOnlyList<Section> NotCounted);

/// <summary>
/// Splits courses' sections into <see cref="ComponentGroup"/>s (rules 7-10), applying pins, and finds the
/// mismatches of rule 8a. Pure and static.
/// </summary>
public static class ComponentGrouping
{
    /// <summary>
    /// Builds the groups. A course with no or one component gets one group of all its covered sections, whatever
    /// their type (rule 7). A course with two or more gets one group per component; its other sections are not
    /// counted (rule 8), and if any component has no covered section the course is left out and the component
    /// reported missing (rule 10).
    /// </summary>
    /// <param name="inputs">The semester view (for components and the missing check).</param>
    /// <param name="covered">The sections under analysis; courses with none are skipped.</param>
    /// <param name="courseIds">The courses to group, in the order the groups should come out.</param>
    /// <param name="pins">Pinned section ids.</param>
    /// <returns>The groups, missing components and not-counted sections.</returns>
    public static GroupingResult Build(
        AccessInputs inputs, IReadOnlyList<Section> covered, IEnumerable<string> courseIds, IReadOnlySet<string> pins)
    {
        var groups = new List<ComponentGroup>();
        var missing = new List<MissingComponent>();
        var notCounted = new List<Section>();
        var byCourse = covered.Where(s => s.CourseId is not null)
            .GroupBy(s => s.CourseId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s, SectionOrder.Comparer).ToList());

        foreach (var courseId in courseIds)
        {
            if (!byCourse.TryGetValue(courseId, out var mine) || mine.Count == 0) continue;
            var components = inputs.ComponentsOf(courseId);
            if (components.Count < 2)
            {
                groups.Add(new ComponentGroup(courseId, null, mine, pins));
                continue;
            }

            var courseGroups = components
                .Select(t => new ComponentGroup(courseId, t, mine.Where(s => s.SectionTypeId == t).ToList(), pins))
                .ToList();
            notCounted.AddRange(mine.Where(s => s.SectionTypeId is null || !components.Contains(s.SectionTypeId)));
            var empty = courseGroups.Where(g => g.All.Count == 0).ToList();
            if (empty.Count > 0)
            {
                var inView = inputs.SectionsByCourse.TryGetValue(courseId, out var all) ? all : [];
                missing.AddRange(empty.Select(g => new MissingComponent(
                    courseId, g.ComponentTypeId!, inView.Any(s => s.SectionTypeId == g.ComponentTypeId))));
                continue;
            }
            groups.AddRange(courseGroups);
        }
        return new GroupingResult(groups, missing, notCounted);
    }

    /// <summary>
    /// Rule 8a: sections whose type is a component type that their course does not list (rule 7 would count them
    /// as alternatives and make access look better than it is). Only once the access setup is done; before that
    /// the suggestion lists every type in the view, so nothing can mismatch.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="courseIds">The courses to check, in output order.</param>
    /// <returns>One entry per course and offending type, in course then section-type order.</returns>
    public static IReadOnlyList<ComponentMismatch> Mismatches(AccessInputs inputs, IEnumerable<string> courseIds)
    {
        var result = new List<ComponentMismatch>();
        if (!inputs.SetUp) return result;
        foreach (var courseId in courseIds)
        {
            if (!inputs.SectionsByCourse.TryGetValue(courseId, out var mine)) continue;
            var components = inputs.ComponentsOf(courseId);
            var bad = mine.Where(s => s.SectionTypeId is not null && inputs.IsComponentType(s.SectionTypeId)
                                      && !components.Contains(s.SectionTypeId)).ToList();
            foreach (var type in inputs.SectionTypes.Where(t => bad.Any(s => s.SectionTypeId == t.Id)))
                result.Add(new ComponentMismatch(courseId, type.Id, bad.Where(s => s.SectionTypeId == type.Id).ToList()));
        }
        return result;
    }
}
