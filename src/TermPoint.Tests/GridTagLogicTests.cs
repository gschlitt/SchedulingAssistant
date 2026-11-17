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

    // ── The multi-tag mark ───────────────────────────────────────────────────────

    /// <summary>A snapshot with tags h (Honours), l (Lab), o (Online) ticked in that order.</summary>
    /// <param name="any">True for "any of the checked tags".</param>
    /// <param name="ticked">Which of h, l, o are ticked, in the filter's order.</param>
    private static FilterSnapshot MarkSnapshot(bool any, string ticked)
    {
        var names = new Dictionary<string, string> { ["h"] = "Honours", ["l"] = "Lab", ["o"] = "Online" };
        var ids = ticked.Length == 0 ? Array.Empty<string>() : ticked.Split(',');
        return Snapshot(ids, any) with { TickedTagsInOrder = ids.Select(id => (id, names[id])).ToList() };
    }

    /// <summary>
    /// The mark (the tag names) shows only in "any" mode, with two or more tags ticked, on a section carrying two or
    /// more of the ticked ones; unticked tags don't count; names follow the filter's order, not the section's.
    /// </summary>
    [Theory]
    // any,   ticked,  section's tags, expected names
    [InlineData(true,  "h,l",   "h,l",   "Honours, Lab")]
    [InlineData(true,  "h,l",   "l,h",   "Honours, Lab")]      // filter order, not the section's
    [InlineData(true,  "h,l",   "h",     "")]                  // only one of them
    [InlineData(true,  "h,l",   "h,o",   "")]                  // o carried but not ticked
    [InlineData(true,  "h,l,o", "h,l,o", "Honours, Lab, Online")]
    [InlineData(true,  "h,l,o", "l,o",   "Lab, Online")]
    [InlineData(true,  "h",     "h,l",   "")]                  // one tag ticked: nothing to overlap
    [InlineData(true,  "",      "h,l",   "")]                  // no tag filter
    [InlineData(false, "h,l",   "h,l",   "")]                  // "all" mode: every shown section has them all
    public void MultiTagNames_AnyModeTwoTickedTagsCarried(bool any, string ticked, string sectionTags, string expected)
    {
        var tags = sectionTags.Length == 0 ? Array.Empty<string>() : sectionTags.Split(',');

        Assert.Equal(expected, MarkSnapshot(any, ticked).MultiTagNames(tags));
    }

    /// <summary>A snapshot built without the ticked-tag list (older callers) never marks a section.</summary>
    [Fact]
    public void MultiTagNames_WithoutTheTickedList_IsEmpty()
    {
        Assert.Equal(string.Empty, Snapshot(["h", "l"], any: true).MultiTagNames(["h", "l"]));
    }

    /// <summary>The filter view model lists the ticked tags in its own order, ids with names.</summary>
    [Fact]
    public void FilterViewModel_TickedTagsInOrder_FollowsTheFilterList()
    {
        var vm = FilterWithTwoTags();
        vm.Tags[1].IsSelected = true;
        vm.Tags[0].IsSelected = true;

        Assert.Equal([(vm.Tags[0].Id, vm.Tags[0].Name), (vm.Tags[1].Id, vm.Tags[1].Name)], vm.TickedTagsInOrder);
    }

    /// <summary>The hover wording, approved by Greg on 2026-11-17.</summary>
    [Fact]
    public void MultiTagHoverText_IsTheApprovedWording()
    {
        Assert.Equal("Tagged with more than one of the tags shown: BIOY3, CHEMY3.",
            ScheduleGridViewModel.MultiTagHoverText("BIOY3, CHEMY3"));
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

    /// <summary>
    /// "Clear all tags" is enabled only while a tag is ticked; it unticks every tag, re-runs the grid once and keeps
    /// the all/any choice.
    /// </summary>
    [Fact]
    public void ClearTags_UnticksEveryTag_RerunsTheGridOnce_KeepsTheLogic()
    {
        var vm = FilterWithTwoTags();
        Assert.False(vm.ClearTagsCommand.CanExecute(null));

        vm.Tags[0].IsSelected = true;
        vm.Tags[1].IsSelected = true;
        vm.TagsMatchAny = true;
        Assert.True(vm.ClearTagsCommand.CanExecute(null));

        var filterChanges = 0;
        vm.FilterChanged += () => filterChanges++;
        vm.ClearTagsCommand.Execute(null);

        Assert.DoesNotContain(vm.Tags, t => t.IsSelected);
        Assert.Equal(1, filterChanges);
        Assert.True(vm.TagsMatchAny);
        Assert.False(vm.ClearTagsCommand.CanExecute(null));
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
