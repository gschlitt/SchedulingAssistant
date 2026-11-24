using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="AccessInputs"/>: which section types are components, how a course's components are resolved
/// (stored list, suggestion, name list), and how a <see cref="Proposal"/> is laid over the saved sections.
/// </summary>
public sealed class AccessInputsTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Seen(string course, params string[] types) =>
        new Dictionary<string, IReadOnlySet<string>> { [course] = types.ToHashSet() };

    // ── Component types ──────────────────────────────────────────────────────

    [Fact]
    public void IsComponentType_WhenSetUp_UsesTheStoredFlag()
    {
        var types = Types();
        types.Single(t => t.Id == Online).IsComponent = true;
        types.Single(t => t.Id == Lab).IsComponent = false;
        var inputs = Inputs([], [], true, null, null, types);
        Assert.True(inputs.IsComponentType(Online));
        Assert.False(inputs.IsComponentType(Lab));
        Assert.True(inputs.IsComponentType(Lecture));
        Assert.False(inputs.IsComponentType("deleted-type"));
        Assert.False(inputs.IsComponentType(null));
    }

    [Fact]
    public void IsComponentType_BeforeSetup_UsesTheNameList()
    {
        var types = Types();
        types.Single(t => t.Id == Online).IsComponent = true;   // ignored before setup
        types.Single(t => t.Id == Lab).IsComponent = false;     // ignored before setup
        var inputs = Inputs([], [], false, null, null, types);
        Assert.False(inputs.IsComponentType(Online));
        Assert.True(inputs.IsComponentType(Lab));
    }

    // ── Component resolution ─────────────────────────────────────────────────

    [Fact]
    public void ComponentsOf_StoredList_SortedBySectionTypeOrder()
    {
        var inputs = Inputs([], [Crs("C", Discussion, Lecture, Lab)]);
        Assert.Equal([Lecture, Lab, Discussion], inputs.ComponentsOf("C"));
    }

    [Fact]
    public void ComponentsOf_StoredList_IgnoresDeletedAndNoLongerComponentTypes()
    {
        var inputs = Inputs([], [Crs("C", Lecture, "deleted-type", Online)]);   // Online is not a component type
        Assert.Equal([Lecture], inputs.ComponentsOf("C"));
    }

    [Fact]
    public void ComponentsOf_EmptyStoredList_IsDifferentFromNever_Set()
    {
        var sections = new[] { Sec("C", "A", Lecture), Sec("C", "L", Lab) };
        var empty = Inputs(sections, [Crs("C")]);                  // stored empty list: students take one section
        var unset = Inputs(sections, [UnsetCrs("C")]);             // never set: the suggestion
        Assert.Empty(empty.ComponentsOf("C"));
        Assert.Equal([Lecture, Lab], unset.ComponentsOf("C"));
    }

    [Fact]
    public void ComponentsOf_NeverSet_SuggestionIsSeenInAnySemesterPlusThisView()
    {
        var inputs = Inputs([Sec("C", "A", Lecture)], [UnsetCrs("C")], true, Seen("C", Lab, Online));
        // Lab seen in another semester, Lecture in this view, Online is not a component type
        Assert.Equal([Lecture, Lab], inputs.ComponentsOf("C"));
    }

    [Fact]
    public void ComponentsOf_UnknownCourse_FallsBackToTheSuggestion()
    {
        var inputs = Inputs([Sec("X", "A", Lecture), Sec("X", "L", Lab)], []);
        Assert.Equal([Lecture, Lab], inputs.ComponentsOf("X"));
    }

    [Fact]
    public void ComponentsOf_BeforeSetup_IgnoresTheStoredListAndUsesSuggestionWithNameList()
    {
        var sections = new[] { Sec("C", "A", Lecture), Sec("C", "L", Lab) };
        var inputs = Inputs(sections, [Crs("C", Lecture)], setUp: false);   // stored list ignored before setup
        Assert.Equal([Lecture, Lab], inputs.ComponentsOf("C"));
    }

    // ── Proposals ────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyProposal_IsTheSavedState()
    {
        var saved = new[] { Sec("A", "1", Lecture), Sec("B", "1", Lecture) };
        var inputs = Inputs(saved, [Crs("A", Lecture), Crs("B", Lecture)]);
        Assert.True(inputs.Proposal.IsEmpty);
        Assert.Equal(["A 1", "B 1"], Ids(inputs.Sections));
        Assert.Equal(["A 1", "B 1"], Ids(inputs.With(Proposal.Empty).Sections));
    }

    [Fact]
    public void Proposal_UpsertReplacesInPlace_AddsNew_AndRemoves()
    {
        var a1 = Sec("A", "1", Lecture, At(0, 9, 10));
        var b1 = Sec("B", "1", Lecture, At(0, 9, 10));
        var inputs = Inputs([a1, b1], [Crs("A", Lecture), Crs("B", Lecture)]);

        var moved = Sec("A", "1", Lecture, At(1, 9, 10));
        var added = Sec("A", "2", Lecture, At(2, 9, 10));
        var view = inputs.With(new Proposal([moved, added], ["B 1"]));

        Assert.Equal(["A 1", "A 2"], Ids(view.Sections));
        Assert.Same(moved, view.Sections[0]);
        Assert.Equal(["A 1", "A 2"], Ids(view.SectionsByCourse["A"]));
        Assert.False(view.SectionsByCourse.ContainsKey("B"));
        // the original inputs are unchanged
        Assert.Equal(["A 1", "B 1"], Ids(inputs.Sections));
    }

    [Fact]
    public void With_ReplacesTheOldProposalInsteadOfComposingWithIt()
    {
        var inputs = Inputs([Sec("A", "1", Lecture), Sec("B", "1", Lecture)], [Crs("A", Lecture), Crs("B", Lecture)]);
        var removedA = inputs.With(new Proposal([], ["A 1"]));
        var removedB = removedA.With(new Proposal([], ["B 1"]));
        Assert.Equal(["B 1"], Ids(removedA.Sections));
        Assert.Equal(["A 1"], Ids(removedB.Sections));
    }

    [Fact]
    public void Proposal_CanChangeASectionsTypeAndCourse_AndComponentsFollow()
    {
        var saved = new[] { Sec("C", "A", Lecture), Sec("C", "X", Online) };
        var inputs = Inputs(saved, [UnsetCrs("C")]);
        Assert.Equal([Lecture], inputs.ComponentsOf("C"));
        var view = inputs.With(new Proposal([Sec("C", "X", Lab)], []));   // X becomes a lab (rule 30)
        Assert.Equal([Lecture, Lab], view.ComponentsOf("C"));
    }
}
