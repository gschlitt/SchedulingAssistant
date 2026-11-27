using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// The picture of an open watch on the grid: the pure builder (<see cref="GridAccessPicture"/>), then the real
/// <see cref="ScheduleGridViewModel"/> over a temp database (which sections it shows, how it stamps them, redrawing
/// when the watch changes, the tooltip) and the right-click menu's Pin item.
/// </summary>
public sealed class AccessGridPictureTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly FocusService _focus;
    private readonly AccessState _state;
    private readonly AccessPicture _picture;
    private readonly ScheduleGridViewModel _grid;
    private readonly SectionRepository _sectionRepo;
    private readonly SchedulingEnvironmentValue _tag = new() { Name = "W", IsAccessWatch = true };
    private readonly string _fall;

    /// <summary>
    /// Seed: tag "W" on courses A (A1 Monday 8:30, A2 Tuesday) and B (B1 Monday 8:30, B2 Wednesday), so A1 and B1
    /// conflict and there are three slates; course X (X1 Thursday) is not in the watch.
    /// </summary>
    public AccessGridPictureTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        var semesterRepo = new SemesterRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);

        foreach (var code in new[] { "A", "B", "X" })
            courseRepo.Insert(new Course { Id = "c-" + code, CalendarCode = code, Title = code });
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        semesterRepo.Insert(fall);
        _fall = fall.Id;
        envRepo.Insert(SchedulingEnvironmentTypes.Tag, _tag);

        void Seed(string course, string code, int day, bool tagged = true) =>
            _sectionRepo.Insert(new Section
            {
                CourseId = "c-" + course, SemesterId = _fall, SectionCode = code, TagIds = tagged ? [_tag.Id] : [],
                Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 510, DurationMinutes = 90 }],
            });
        Seed("A", "A1", 1); Seed("A", "A2", 2); Seed("B", "B1", 1); Seed("B", "B2", 3); Seed("X", "X1", 4, tagged: false);

        _focus = new FocusService(subjectRepo, courseRepo, _sectionRepo, semesterRepo, ayRepo, _semesterContext, _sectionStore, null);
        _state = AccessStateTestSupport.For(db, _sectionStore, _notifier);
        _picture = new AccessPicture(_state);
        _grid = new ScheduleGridViewModel(
            _sectionRepo, courseRepo, new InstructorRepository(db), new RoomRepository(db), subjectRepo, envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), _notifier, new InstructorCommitmentRepository(db),
            _fx.Lock, new SharedScheduleService(),
            _focus, FocusTestSupport.NoColours(courseRepo, subjectRepo, _sectionStore), new BlockPatternRepository(db),
            new FakeSectionCardState(), new LegalStartTimeRepository(db), new SectionMarks(), _state, _picture);

        _semesterContext.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { _fall });
        _sectionStore.Reload(_sectionRepo, [_fall]);
    }

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    private IEnumerable<TileEntry> Entries => _grid.GridData.DayColumns.SelectMany(c => c.Tiles).SelectMany(t => t.Entries);

    private Section Get(string code) => _sectionRepo.GetAll(_fall).Single(s => s.SectionCode == code);

    private TileEntry EntryOf(string code) => Entries.Single(e => e.SectionId == Get(code).Id);

    // ── The pure builder ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, AccessTileLook> Looks(
        AccessInputs inputs, Analysis analysis, IReadOnlyList<Section>? slate = null) =>
        GridAccessPicture.Build(inputs, analysis, "W", inputs.Sections, slate, Cautions.For(analysis), false, id => id ?? "none");

    [Fact]
    public void Stranded_Essential_AndUsableSections_SayWhy()
    {
        var inputs = Simple([
            Sec("A", "A1", meetings: At(0, 9, 10)), Sec("A", "A2", meetings: At(1, 9, 10)),
            Sec("B", "B1", meetings: At(0, 9, 10)), Sec("B", "B2", meetings: At(0, 9, 10)),
        ]);
        var looks = Looks(inputs, AnalyzeAll(inputs));

        Assert.True(looks["A A1"].Stranded);
        Assert.Equal("A A1 can't be part of any slate for **W**: no combination of the other courses fits around it.", looks["A A1"].Hover);
        Assert.True(looks["A A2"].Essential);
        Assert.Equal("●", looks["A A2"].Mark);
        Assert.Equal(
            "Every slate for **W** uses A A2: A's other sections can't fit. If it moves to a conflicting time, students lose every full slate.",
            looks["A A2"].Hover);
        Assert.Equal("B B1 can be part of a slate for **W**.", looks["B B1"].Hover);
        Assert.False(looks["B B1"].Faded);
        Assert.Equal(string.Empty, looks["B B1"].Mark);
    }

    /// <summary>
    /// A course left out for a missing component (rules 10, 6d) is faded with a hover naming the component, whether or
    /// not the rest of the watch has a slate (Greg, 2026-11-25: with no full slate it was drawn plainly, no hover).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACourseMissingAComponent_IsFaded_AndSaysWhich(bool restHasSlate)
    {
        var inputs = Simple([
            Sec("M", "L0A", type: Lab, meetings: At(1, 14, 16)),
            Sec("A", "A1", meetings: At(0, 9, 10)),
            Sec("B", "B1", meetings: At(restHasSlate ? 2 : 0, 9, 10)),
        ], Crs("M", Lecture, Lab));
        var looks = Looks(inputs, AnalyzeAll(inputs));

        Assert.True(looks["M L0A"].Faded);
        Assert.Equal("M L0A isn't counted: M's lecture sections aren't in **W**.", looks["M L0A"].Hover);
    }

    [Fact]
    public void ASectionAlone_InItsComponent_IsTheOnlySection()
    {
        var inputs = Simple([Sec("A", "A1", meetings: At(0, 9, 10)), Sec("B", "B1", meetings: At(1, 9, 10))]);
        var looks = Looks(inputs, AnalyzeAll(inputs));
        Assert.Equal("A A1 is the only A section, so every slate uses it.", looks["A A1"].Hover);
    }

    [Fact]
    public void PinsAndTicks_FadeTheSectionsLeftOut()
    {
        var inputs = Simple([
            Sec("A", "A1", meetings: At(0, 9, 10)), Sec("A", "A2", meetings: At(1, 9, 10)),
            Sec("B", "B1", meetings: At(2, 9, 10)),
        ]);
        var pinned = Looks(inputs, AnalyzeAll(inputs, pins: new HashSet<string> { "A A1" }));
        Assert.True(pinned["A A1"].Pinned);
        Assert.Equal("A A1 is pinned: only slates with it count.", pinned["A A1"].Hover);
        Assert.True(pinned["A A2"].Faded);
        Assert.Equal("Not used: A A1 is pinned.", pinned["A A2"].Hover);

        var subset = Looks(inputs, AnalyzeAll(inputs, subset: new HashSet<string> { "A" }));
        Assert.True(subset["B B1"].Faded);
        Assert.Equal("B is unticked, so it isn't part of these slates.", subset["B B1"].Hover);
        Assert.False(subset["A A1"].Faded);
    }

    [Fact]
    public void TheSlateBeingShown_IsAGhost_AndTheRestFade()
    {
        var inputs = Simple([
            Sec("A", "A1", meetings: At(0, 9, 10)), Sec("A", "A2", meetings: At(1, 9, 10)),
            Sec("B", "B1", meetings: At(2, 9, 10)),
        ]);
        var analysis = AnalyzeAll(inputs);
        var slate = new SlateSearch(analysis.Groups, analysis.OneCampusRule).List(10)[0];
        var looks = Looks(inputs, analysis, slate);

        Assert.True(looks["A A1"].InSlate);
        Assert.Equal("A A1 is in this slate.", looks["A A1"].Hover);
        Assert.True(looks["B B1"].InSlate);
        Assert.True(looks["A A2"].Faded);
    }

    [Fact]
    public void WithNoFullSlate_TheSectionsToMoveAreMarked()
    {
        var inputs = Simple([Sec("A", "A1", meetings: At(0, 9, 10)), Sec("B", "B1", meetings: At(0, 9, 10))]);
        var looks = Looks(inputs, AnalyzeAll(inputs));

        Assert.True(looks["A A1"].CantFit);
        Assert.Equal("▲", looks["A A1"].Mark);
        Assert.Equal("A A1 conflicts with B B1, so **W** has no full slate. Try moving A A1 or B B1.", looks["A A1"].Hover);
    }

    /// <summary>
    /// The CHBE-Y3 case (Greg, 2026-11-25): two courses whose only discussions clash. Only the discussions get the ▲;
    /// the lectures fit fine and are drawn plainly.
    /// </summary>
    [Fact]
    public void WithNoFullSlate_OnlyTheSectionsInTheClashGetTheTriangle()
    {
        var inputs = Simple([
            Sec("C346", "101", type: Lecture, meetings: At([0, 2, 4], 10, 11)),
            Sec("C346", "T1A", type: Discussion, meetings: At(0, 14, 16)),
            Sec("C352", "101", type: Lecture, meetings: At([0, 1, 3], 12.5, 14)),
            Sec("C352", "T1A", type: Discussion, meetings: At(0, 14, 16)),
        ], Crs("C346", Lecture, Discussion), Crs("C352", Lecture, Discussion));
        var looks = Looks(inputs, AnalyzeAll(inputs));

        Assert.True(looks["C346 T1A"].CantFit);
        Assert.True(looks["C352 T1A"].CantFit);
        Assert.False(looks["C346 101"].CantFit);
        Assert.False(looks["C352 101"].CantFit);
        Assert.Equal(string.Empty, looks["C346 101"].Hover);
    }

    [Fact]
    public void ACautionedSection_GetsTheDiamond()
    {
        var sections = new List<Section>
        {
            Sec("A", "1", meetings: At(1, 9, 10)), Sec("A", "2", meetings: At(0, 9, 10)), Sec("B", "1", meetings: At(2, 9, 10)),
        };
        for (int i = 2; i <= 7; i++) sections.Add(Sec("B", i.ToString(), meetings: At(1, 9, 10)));
        var inputs = Simple(sections);
        var looks = Looks(inputs, AnalyzeAll(inputs));

        Assert.True(looks["A 1"].Caution);
        Assert.Equal("◆", looks["A 1"].Mark);
        Assert.StartsWith("A 1 fits only with B 1:", looks["A 1"].Hover);
    }

    // ── The grid ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithNothingOpen_TheGridIsAsBefore_WithNoLooks()
    {
        Assert.Equal(5, Entries.Select(e => e.SectionId).Distinct().Count());
        Assert.All(Entries, e => Assert.Null(e.Access));
    }

    [Fact]
    public void AnOpenWatch_ShowsOnlyItsSections_EachWithItsLook()
    {
        _picture.OpenWatch(_tag.Id);

        Assert.Equal(4, Entries.Select(e => e.SectionId).Distinct().Count());
        Assert.DoesNotContain(Entries, e => e.SectionId == Get("X1").Id);
        Assert.All(Entries, e => Assert.NotNull(e.Access));
        Assert.Equal("A A1 is in this slate.", EntryOf("A1").Access!.Hover);   // three slates: the first is shown
        Assert.False(EntryOf("A1").Access!.Faded);
    }

    [Fact]
    public void OpeningAWatch_SetsTheRealFilterBarToItsTag()
    {
        _picture.OpenWatch(_tag.Id);

        Assert.Equal([_tag.Id], _grid.Filter.SelectedTagIds);
        Assert.True(_grid.Filter.IsActive);
        Assert.Contains("W", _grid.Filter.ActiveSummary);
    }

    [Fact]
    public void RemovingTheTagFilter_ShowsTheRest_Faded_AndCloseThenLeavesTheFilters()
    {
        _picture.OpenWatch(_tag.Id);
        _grid.Filter.ClearFilters();

        Assert.Equal(5, Entries.Select(e => e.SectionId).Distinct().Count());
        Assert.True(EntryOf("X1").Access!.Faded);
        Assert.False(EntryOf("A1").Access!.Faded);   // A1 is in the slate being shown (stepping starts by itself)

        // The user changed the filters while the watch was open: they are theirs now, so Close leaves them.
        _picture.Close();
        Assert.Empty(_grid.Filter.SelectedTagIds);
        Assert.All(Entries, e => Assert.Null(e.Access));
    }

    [Fact]
    public void Close_PutsTheUsersOwnFiltersBack_WhenTheyWereNotTouched()
    {
        var own = _grid.Filter.SelectionOf([], ["c-B"]);
        _grid.Filter.ApplySelection(own);

        _picture.OpenWatch(_tag.Id);
        Assert.Empty(_grid.Filter.SelectedCourseIds);

        // Another thing opened over it (a course alone) keeps the user's filters to restore.
        Assert.True(_picture.OpenCourse("c-A"));
        Assert.Equal(["c-A"], _grid.Filter.SelectedCourseIds);
        Assert.Empty(_grid.Filter.SelectedTagIds);

        _picture.Close();
        Assert.True(own.SameAs(_grid.Filter.CaptureSelection()));
    }

    [Fact]
    public void ThePicture_SaysWhenTheFiltersHideSomeOfTheWatchsSections()
    {
        _picture.OpenWatch(_tag.Id);
        Assert.Equal(0, _picture.HiddenSectionCount);

        var withCourse = _grid.Filter.SelectionOf([_tag.Id], ["c-A"]);
        _grid.Filter.ApplySelection(withCourse);

        Assert.True(_picture.HiddenSectionCount > 0);
        Assert.EndsWith("of W's sections are hidden by the filters.", AccessText.HiddenWatchSectionsText(_picture.HiddenSectionCount, _picture.Name, _picture.HiddenByFocus));
    }

    [Fact]
    public void UntickingAndPinning_RedrawTheGrid()
    {
        _picture.OpenWatch(_tag.Id);
        _picture.SetCourseTicked("c-B", false);
        Assert.True(EntryOf("B2").Access!.Faded);
        Assert.Equal("B is unticked, so it isn't part of these slates.", EntryOf("B2").Access!.Hover);
        _picture.TickAll();

        _picture.TogglePin(Get("A1").Id);
        Assert.True(EntryOf("A1").Access!.Pinned);
        Assert.True(EntryOf("A2").Access!.Faded);
    }

    [Fact]
    public void Stepping_ShowsTheSlateAsAGhost()
    {
        _picture.OpenWatch(_tag.Id);   // three slates: stepping starts by itself

        // the first slate is A1 with B2
        Assert.True(EntryOf("A1").Access!.InSlate);
        Assert.True(EntryOf("B2").Access!.InSlate);
        Assert.True(EntryOf("A2").Access!.Faded);
        Assert.True(EntryOf("B1").Access!.Faded);

        _picture.Step(1); // A2 with B1
        Assert.True(EntryOf("A2").Access!.InSlate);
        Assert.True(EntryOf("B1").Access!.InSlate);
        Assert.True(EntryOf("A1").Access!.Faded);

    }

    [Fact]
    public void ClosingTheWatch_BringsBackTheUsualGrid()
    {
        _picture.OpenWatch(_tag.Id);
        _picture.Close();

        Assert.Equal(5, Entries.Select(e => e.SectionId).Distinct().Count());
        Assert.All(Entries, e => Assert.Null(e.Access));
    }

    [Fact]
    public void ACourseOpenedAlone_ShowsOnlyThatCourse()
    {
        _picture.OpenCourse("c-A");
        Assert.Equal(["A1", "A2"], Entries.Select(e => _sectionRepo.GetById(e.SectionId)!.SectionCode).Distinct().OrderBy(x => x).ToArray());
    }

    [Fact]
    public void TheTooltip_AppendsWhatTheWatchSays()
    {
        _picture.OpenWatch(_tag.Id);
        var tile = _grid.GridData.DayColumns.SelectMany(c => c.Tiles)
            .First(t => t.Entries.Any(e => e.SectionId == Get("A2").Id));

        _picture.Step(1);   // the second slate is A2 with B1
        tile = _grid.GridData.DayColumns.SelectMany(c => c.Tiles).First(t => t.Entries.Any(e => e.SectionId == Get("A2").Id));
        var tip = ScheduleGridViewModel.BuildTileTooltip(tile, _grid.GridData.ConflictHoverLines, 2);
        Assert.Contains("A A2 is in this slate.", tip.Lines);
        // A single Access line is enough for the tile to show its tooltip (Greg, 2026-11-25: a faded section had none).
        Assert.True(tip.IsShownOnTile);
        Assert.False(new TileTooltip(["0830-1000"]).IsShownOnTile);
    }

    /// <summary>
    /// With the open watch's conflicts marked, a line the watch's own hover would repeat shows the mark's line alone
    /// (Greg, 2026-11-25); a day with no mark keeps the watch's line.
    /// </summary>
    [Fact]
    public void TheTooltip_DoesNotRepeatAMarkedConflict()
    {
        _picture.OpenWatch(_tag.Id);
        _state.MarkAllConflicts = true; // A1 and B1 conflict harmlessly on Monday
        var tiles = _grid.GridData.DayColumns.SelectMany(c => c.Tiles).ToList();
        var monday = tiles.First(t => t.Entries.Any(e => e.SectionId == Get("A1").Id));
        var tuesday = tiles.First(t => t.Entries.Any(e => e.SectionId == Get("A2").Id));

        var tip = ScheduleGridViewModel.BuildTileTooltip(monday, _grid.GridData.ConflictHoverLines, 1,
            _grid.GridData.RepeatedAccessHovers);
        Assert.Contains(tip.Lines, l => l.StartsWith("A A1 conflicts with B B1."));

        var other = ScheduleGridViewModel.BuildTileTooltip(tuesday, _grid.GridData.ConflictHoverLines, 2,
            _grid.GridData.RepeatedAccessHovers);
        Assert.DoesNotContain(other.Lines, l => l.StartsWith("A A2 conflicts"));
    }

    /// <summary>Which conflict marks a look's hover repeats: the same news about the same section, and nothing else.</summary>
    [Fact]
    public void ALook_IsRepeatedOnlyByAMarkSayingTheSame()
    {
        var a = Sec("A", "A1"); var b = Sec("B", "B1");
        ConflictMark Mark(ConflictMarkKind kind, Section? x = null) => new(kind, a, b, [], x, []);
        var stranded = new AccessTileLook(Stranded: true, Hover: "h");

        Assert.True(stranded.IsRepeatedBy(Mark(ConflictMarkKind.Stranded, a), a.Id));
        Assert.False(stranded.IsRepeatedBy(Mark(ConflictMarkKind.Stranded, b), a.Id));
        Assert.True(new AccessTileLook(CantFit: true, Hover: "h").IsRepeatedBy(Mark(ConflictMarkKind.Move), a.Id));
        Assert.False(new AccessTileLook(Hover: "h").IsRepeatedBy(Mark(ConflictMarkKind.Move), a.Id));
        Assert.True(new AccessTileLook(Usable: true, Hover: "h").IsRepeatedBy(Mark(ConflictMarkKind.Harmless), a.Id));
        Assert.False(new AccessTileLook(Essential: true, Hover: "h").IsRepeatedBy(Mark(ConflictMarkKind.Other), a.Id));
        Assert.False(new AccessTileLook(Stranded: true).IsRepeatedBy(Mark(ConflictMarkKind.Stranded, a), a.Id));
    }

    [Fact]
    public void Stepping_FollowsTheWatch_AndPinsAndTicksReshapeIt()
    {
        Assert.False(_picture.IsOpen);

        _picture.OpenWatch(_tag.Id);
        Assert.True(_picture.IsStepping);
        Assert.Equal("Slate 1 of 3", AccessText.SlatePosition(_picture.SlateIndex, _picture.Slates.Count, false));
        _picture.Step(1);
        Assert.Equal(1, _picture.SlateIndex);

        _picture.TogglePin(Get("A1").Id);
        Assert.Single(_picture.PinnedSectionIds);
        _picture.ClearPins();
        Assert.Empty(_picture.PinnedSectionIds);

        _picture.SetCourseTicked("c-B", false);
        Assert.Equal(["A", "B"], _picture.Slates.Count > 1 ? ["A", "B"] : []);   // A's two sections are still two slates

        _picture.Close();
        Assert.False(_picture.IsOpen);
    }

    [Fact]
    public void TheContextMenu_OffersPinOnlyForASectionOfTheOpenWatch()
    {
        var menu = _grid.ContextMenu;
        _grid.PrepareContextMenu(Get("A1").Id, 1, 510);
        Assert.False(menu.HasPinItem);

        _picture.OpenWatch(_tag.Id);
        _grid.PrepareContextMenu(Get("A1").Id, 1, 510);
        Assert.True(menu.HasPinItem);
        Assert.Equal("Pin in slates for W", menu.PinItemText);

        _grid.PrepareContextMenu(Get("X1").Id, 4, 510);
        Assert.False(menu.HasPinItem); // not in the watch

        _grid.PrepareContextMenu(Get("A1").Id, 1, 510);
        menu.TogglePinCommand.Execute(null);
        Assert.True(_picture.IsPinned(Get("A1").Id));
        _grid.PrepareContextMenu(Get("A1").Id, 1, 510);
        Assert.Equal("Unpin", menu.PinItemText);
    }
}
