using TermPoint.Models;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 31: the schedule grid's Tags filter can match ALL of the checked tags (the default, the old behaviour) or
/// ANY of them. Covers the rule itself (<see cref="FilterSnapshot.PassesTagFilter"/>) and the filter view model's radio
/// pair, change events, summary and reset. The popup's radio buttons and the "Tags (any of N)" label are view code and
/// are checked by hand.
/// </summary>
public class GridTagLogicTests
{
    // ── The rule ────────────────────────────────────────────────────────────────

    /// <summary>A snapshot with only the Tags dimension set.</summary>
    /// <param name="tagIds">The checked tags.</param>
    /// <param name="any">True for "any of the checked tags".</param>
    private static FilterSnapshot Snapshot(string[] tagIds, bool any) => new(
        NamedInstructorIds: new(), NamedRoomIds: new(), SubjectIds: new(), CampusIds: new(), SectionTypeIds: new(),
        TagIds: tagIds.ToHashSet(), MeetingTypeIds: new(), LevelIds: new(), CourseIds: new(),
        NotStaffedSelected: false, EmphasizeUnstaffedSelected: false, UnroomedSelected: false,
        HasOverlay: false, OverlayType: null, SelectedOverlayId: null, TagsMatchAny: any);

    /// <summary>
    /// With tags checked, "all" needs every one of them on the section, "any" needs at least one; with none checked,
    /// every section passes either way.
    /// </summary>
    [Theory]
    // checked tags,   section's tags,      all,   any
    [InlineData("h,l", "h,l,o",             true,  true)]
    [InlineData("h,l", "h",                 false, true)]
    [InlineData("h,l", "o",                 false, false)]
    [InlineData("h,l", "",                  false, false)]
    [InlineData("h",   "h",                 true,  true)]
    [InlineData("",    "o",                 true,  true)]
    [InlineData("",    "",                  true,  true)]
    public void PassesTagFilter_AllNeedsEveryTag_AnyNeedsOne(string checkedTags, string sectionTags, bool passesAll, bool passesAny)
    {
        static string[] Split(string s) => s.Length == 0 ? Array.Empty<string>() : s.Split(',');

        Assert.Equal(passesAll, Snapshot(Split(checkedTags), any: false).PassesTagFilter(Split(sectionTags)));
        Assert.Equal(passesAny, Snapshot(Split(checkedTags), any: true).PassesTagFilter(Split(sectionTags)));
    }

    /// <summary>A snapshot built without the new argument matches "all", the behaviour before item 31.</summary>
    [Fact]
    public void ASnapshotBuiltWithoutTheFlag_MatchesAll()
    {
        var snapshot = new FilterSnapshot(new(), new(), new(), new(), new(), new() { "h", "l" }, new(), new(), new(),
            false, false, false, false, null, null);

        Assert.False(snapshot.TagsMatchAny);
        Assert.False(snapshot.PassesTagFilter(new[] { "h" }));
        Assert.True(snapshot.PassesTagFilter(new[] { "h", "l" }));
    }

    // ── The filter view model ──────────────────────────────────────────────────

    /// <summary>A filter view model offering two tags, "Honours" (h) and "Lab" (l).</summary>
    private static GridFilterViewModel FilterWithTwoTags()
    {
        var vm = new GridFilterViewModel();
        vm.PopulateOptions(
            new Dictionary<string, Instructor>(), new Dictionary<string, Room>(), new Dictionary<string, Subject>(),
            new Dictionary<string, Campus>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["h"] = new() { Id = "h", Name = "Honours" },
                ["l"] = new() { Id = "l", Name = "Lab" },
            },
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, string>(),
            new Dictionary<string, Course>());
        return vm;
    }

    /// <summary>
    /// Choosing "any" updates both radio buttons, re-runs the grid once and says "any" in the summary; un-checking a
    /// button (what the radio group does to the other one) and choosing the same value again change nothing.
    /// </summary>
    [Fact]
    public void ChoosingAny_UpdatesTheButtonsTheGridAndTheSummary_Once()
    {
        var vm = FilterWithTwoTags();
        vm.Tags[0].IsSelected = true;
        Assert.True(vm.TagsMatchAll);
        Assert.False(vm.TagsMatchAny);

        var filterChanges = 0;
        var properties = new List<string?>();
        vm.FilterChanged += () => filterChanges++;
        vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        vm.TagsMatchAny = true;
        vm.TagsMatchAll = false;   // the radio group un-checks the other button: ignored
        vm.TagsMatchAny = true;    // same value: nothing more

        Assert.True(vm.TagsMatchAny);
        Assert.False(vm.TagsMatchAll);
        Assert.Equal(1, filterChanges);
        Assert.Contains(nameof(GridFilterViewModel.TagsMatchAny), properties);
        Assert.Contains(nameof(GridFilterViewModel.TagsMatchAll), properties);
        Assert.Contains("Tags (any): ", vm.ActiveSummary);
    }

    /// <summary>With "all" (the default) the summary reads as before.</summary>
    [Fact]
    public void WithAll_TheSummaryReadsAsBefore()
    {
        var vm = FilterWithTwoTags();
        vm.Tags[0].IsSelected = true;

        Assert.StartsWith("Tags: ", vm.ActiveSummary);
    }

    /// <summary>Clear starts afresh: the tags are unchecked and the logic goes back to "all".</summary>
    [Fact]
    public void Clear_ResetsTheLogicToAll()
    {
        var vm = FilterWithTwoTags();
        vm.Tags[0].IsSelected = true;
        vm.TagsMatchAny = true;

        vm.ClearFilters();

        Assert.True(vm.TagsMatchAll);
        Assert.False(vm.TagsMatchAny);
        Assert.DoesNotContain(vm.Tags, t => t.IsSelected);
    }
}
