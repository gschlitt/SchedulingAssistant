using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The pre-fills of the access setup wizard (Access Analyzer rules 4-6, revision 8f): which section types look like
/// components, and which of the chosen types a course's sections have. Suggestions only; the user decides. Pure and
/// static, so it compiles and behaves the same in the WASM demo.
/// </summary>
public static class ComponentSuggestion
{
    /// <summary>The section types the wizard pre-ticks: those whose name looks like a component (<see cref="ComponentNames.LooksLikeComponent"/>).</summary>
    /// <param name="sectionTypes">The section types.</param>
    /// <returns>The ids of the pre-ticked types.</returns>
    public static IReadOnlySet<string> PreTickedTypes(IEnumerable<SchedulingEnvironmentValue> sectionTypes) =>
        sectionTypes.Where(t => ComponentNames.LooksLikeComponent(t.Name)).Select(t => t.Id).ToHashSet();

    /// <summary>
    /// The pre-filled components of one course: the section types its sections have in any semester, limited to the
    /// chosen types, in section type order. A course with no sections gets an empty list.
    /// </summary>
    /// <param name="courseId">The course id.</param>
    /// <param name="typeIdsByCourse">For each course id, its sections' type ids in every semester (<c>ISectionRepository.GetSectionTypeIdsByCourse</c>).</param>
    /// <param name="chosen">The section type ids chosen as components.</param>
    /// <param name="sectionTypesInOrder">The section types, in section type order.</param>
    /// <returns>The suggested component type ids.</returns>
    public static IReadOnlyList<string> Prefill(
        string courseId,
        IReadOnlyDictionary<string, IReadOnlySet<string>> typeIdsByCourse,
        IReadOnlySet<string> chosen,
        IReadOnlyList<SchedulingEnvironmentValue> sectionTypesInOrder)
    {
        if (!typeIdsByCourse.TryGetValue(courseId, out var seen)) return [];
        return sectionTypesInOrder
            .OrderBy(t => t.SortOrder)
            .Where(t => chosen.Contains(t.Id) && seen.Contains(t.Id))
            .Select(t => t.Id)
            .ToList();
    }
}
