using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// What Edit Course needs to show its Components group (Access Analyzer revision 8f). Given only once the access
/// checks are set up; without it the group is replaced by a hint and the course's stored components are never touched.
/// </summary>
/// <param name="SectionTypes">Every section type, in section type order (a type's <c>IsComponent</c> decides if it is offered).</param>
/// <param name="Sections">The course's sections in the semester(s) shown now (the "this term" counts and the warnings).</param>
/// <param name="Initial">The ticks to start from: the stored list, or, for a course whose list was never set, the engine's suggestion.</param>
/// <param name="MismatchLines">
/// Rule 8a's sentences for a hypothetical ticked list (section type ids): sections of a component type the course
/// would not list. Returns an empty list when there is nothing to say.
/// </param>
public sealed record CourseComponentsContext(
    IReadOnlyList<SchedulingEnvironmentValue> SectionTypes,
    IReadOnlyList<Section> Sections,
    IReadOnlyList<string> Initial,
    Func<IReadOnlyList<string>, IReadOnlyList<string>> MismatchLines);

/// <summary>One box of Edit Course's Components group: a section type, whether it is ticked, and how many of the course's sections have it this term.</summary>
public sealed partial class ComponentOptionViewModel : ObservableObject
{
    /// <summary>The section type.</summary>
    public SchedulingEnvironmentValue Type { get; }

    /// <summary>The section type's name.</summary>
    public string Name => Type.Name;

    /// <summary>"3 this term" or "none this term".</summary>
    public string CountText { get; }

    /// <summary>Whether the course has this type as a component.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Creates an option.</summary>
    /// <param name="type">The section type.</param>
    /// <param name="selected">Whether it starts ticked.</param>
    /// <param name="countText">The "this term" count text.</param>
    public ComponentOptionViewModel(SchedulingEnvironmentValue type, bool selected, string countText)
    {
        Type = type;
        _isSelected = selected;
        CountText = countText;
    }
}
