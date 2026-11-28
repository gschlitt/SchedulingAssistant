using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// A watch's detail (<see cref="AccessWatchDetailViewModel"/>) over real repositories: header, health line, flags,
/// untagged line, course rows and ticks, pins, Multi-campus box, fold, the Watching button, read-only; plus the
/// pure pieces it uses (<see cref="Cautions"/>, <see cref="AccessText.CandidatePhrases"/>).
/// <para>
/// Seed: courses A and B and the watched tag "W". Fall: A 1 (Monday) and A 2 (Tuesday), B 1 (Monday) and B 2
/// (Wednesday): A 1 and B 1 conflict, three slates. Winter: A 1 and B 1 only, both Monday.
/// </para>
/// </summary>
public sealed class AccessWatchDetailTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SectionStore _store = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly SectionRepository _sectionRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly SemesterContext _semesters = new();
    private readonly AccessState _state;
    private readonly AccessPicture _picture;
    private readonly AccessWatchDetailViewModel _detail;
    private readonly SchedulingEnvironmentValue _tag = new() { Name = "W", IsAccessWatch = true };
    private readonly string _fall;
    private readonly string _winter;
    private readonly string _yearId;
    private bool _canWrite = true;

    public AccessWatchDetailTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);

        foreach (var code in new[] { "A", "B" })
            courseRepo.Insert(new Course { Id = "c-" + code, CalendarCode = code, Title = code });
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        _yearId = year.Id;
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        var winter = new Semester { AcademicYearId = year.Id, Name = "Winter", SortOrder = 1 };
        semesterRepo.Insert(fall);
        semesterRepo.Insert(winter);
        _fall = fall.Id;
        _winter = winter.Id;
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, _tag);

        AddSection(_fall, "A", "1", 1); AddSection(_fall, "A", "2", 2);
        AddSection(_fall, "B", "1", 1); AddSection(_fall, "B", "2", 3);
        AddSection(_winter, "A", "1", 1); AddSection(_winter, "B", "1", 1);

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        _picture = new AccessPicture(_state);
        var toggler = new AccessWatchToggler(_envRepo, _state, _notifier, () => _canWrite);
        _detail = new AccessWatchDetailViewModel(_picture, _state, toggler, _semesters, new CampusRepository(db), _store);
        _semesters.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { _fall });
        _store.Reload(_sectionRepo, [_fall]);
    }

    private Section AddSection(string semesterId, string course, string code, int day, string? campus = null,
        bool tagged = true, bool timed = true)
    {
        var section = new Section
        {
            CourseId = "c-" + course, SemesterId = semesterId, SectionCode = code, TagIds = tagged ? [_tag.Id] : [], CampusId = campus,
            Schedule = timed ? [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }] : [],
        };
        _sectionRepo.Insert(section);
        return section;
    }

    private Section Get(string semesterId, string course, string code) =>
        _sectionRepo.GetAll(semesterId).Single(s => s.CourseId == "c-" + course && s.SectionCode == code);

    private void Reload() => _store.Reload(_sectionRepo, [_fall]);

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    [Fact]
    public void NothingOpen_ShowsNothing()
    {
        Assert.False(_detail.IsOpen);
        Assert.Empty(_detail.Courses);
        Assert.Empty(_detail.Flags);
    }

    [Fact]
    public void AnOpenWatch_ShowsItsHeaderHealthAndCourses()
    {
        _picture.OpenWatch(_tag.Id);

        Assert.True(_detail.IsOpen);
        Assert.False(_detail.IsCheckMode);
        Assert.Equal("W", _detail.Name);
        Assert.Equal("All watches", _detail.BackText);
        Assert.Empty(_detail.SourceText);   // a watch's name is in the header only
        Assert.True(_detail.ShowWatching);
        Assert.True(_detail.CanChange);
        Assert.Equal("3 slates · 1 conflict", _detail.HealthLine);
        Assert.False(_detail.IsHealthAttention);
        Assert.Empty(_detail.Flags);
        Assert.False(_detail.ShowUntagged);
        Assert.True(_detail.HasSlatesToStep);   // three slates: the box shows, but stepping waits for the user
        Assert.False(_detail.IsStepping);
        Assert.False(_detail.ShowStepHint);
        Assert.False(_detail.ShowMultiCampus);
        Assert.Equal(["A", "B"], _detail.Courses.Select(r => r.Code).ToArray());
        Assert.All(_detail.Courses, r => Assert.True(r.IsTicked));
        Assert.Equal("all 2 sections", _detail.Courses[0].Phrases.Single().Text);
        Assert.False(_detail.ShowTickAll);
        Assert.False(_detail.HasSemesterChoice);
    }

    [Fact]
    public void Ticks_NarrowTheHealthLine_TheLastOneStays_AndTickAllRestores()
    {
        _picture.OpenWatch(_tag.Id);
        var b = _detail.Courses.Single(r => r.Code == "B");
        b.IsTicked = false;

        Assert.False(b.IsTicked);
        Assert.True(_detail.ShowTickAll);
        Assert.StartsWith("1 of 2 courses · 2 slates", _detail.HealthLine);
        var a = _detail.Courses.Single(r => r.Code == "A");
        Assert.False(a.IsTickEnabled);
        Assert.Equal("At least one course stays ticked.", a.TickTip);
        Assert.True(b.IsTickEnabled);

        a.IsTicked = false; // refused: the last tick stays
        Assert.True(a.IsTicked);

        _detail.TickAllCommand.Execute(null);
        Assert.False(_detail.ShowTickAll);
        Assert.True(b.IsTicked);
        Assert.Equal("3 slates · 1 conflict", _detail.HealthLine);
    }

    [Fact]
    public void Pins_ShowABanner_AndTheHealthLineCountsThem()
    {
        _picture.OpenWatch(_tag.Id);
        _picture.TogglePin(Get(_fall, "A", "1").Id);

        Assert.True(_detail.HasPins);
        Assert.Equal("A 1", _detail.PinChips.Single().Label);
        Assert.StartsWith("1 section pinned · 1 slate", _detail.HealthLine);
        _detail.PinChips[0].UnpinCommand.Execute(null);
        Assert.False(_detail.HasPins);
        Assert.Equal("3 slates · 1 conflict", _detail.HealthLine);
    }

    [Fact]
    public void PinsThatStopEverySlate_AreNamedInTheHealthLine()
    {
        _picture.OpenWatch(_tag.Id);
        _picture.TogglePin(Get(_fall, "A", "1").Id);
        _picture.TogglePin(Get(_fall, "B", "1").Id); // the two conflict: no slate with both

        Assert.StartsWith("2 sections pinned · No slate with these pins", _detail.HealthLine);
        Assert.True(_detail.IsHealthAttention);
    }

    [Fact]
    public void AStrandedSection_GetsAFlagWithItsReason()
    {
        // B 2 moves onto Monday too: A 1 conflicts with both B sections, so it is in no slate
        var b2 = Get(_fall, "B", "2");
        b2.Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(b2);
        Reload();
        _picture.OpenWatch(_tag.Id);

        var flag = Assert.Single(_detail.Flags, f => !f.IsCaution);
        Assert.Equal("▲", flag.Mark);
        Assert.StartsWith("A 1 is stranded: ", AccessText.Plain(flag.Text));
        Assert.EndsWith(" No student taking all of W can use it.", AccessText.Plain(flag.Text));
        Assert.Contains("1 section stranded", _detail.HealthLine);
        Assert.Contains("depends on A 2", _detail.HealthLine);
    }

    /// <summary>
    /// The "Why" list of a stranded section is shown while exactly that section is the one selected (Greg, 2026-11-27):
    /// C 1 (Tuesday, the only C) rules out A 2, which leaves A 1 as the only A, which rules out B 1: two steps.
    /// </summary>
    [Fact]
    public void AStrandedSectionWithAChain_ShowsItsWhyListOnlyWhileItIsTheSelectedSection()
    {
        new CourseRepository(_fx.Db).Insert(new Course { Id = "c-C", CalendarCode = "C", Title = "C" });
        AddSection(_fall, "C", "1", 2);
        Reload();
        _picture.OpenWatch(_tag.Id);
        var b1 = Get(_fall, "B", "1");
        var a2 = Get(_fall, "A", "2");

        var why = Assert.Single(_detail.Flags, f => f.SectionId == b1.Id);
        var oneStep = Assert.Single(_detail.Flags, f => f.SectionId == a2.Id);
        Assert.Equal(2, why.WhyLines.Count);
        Assert.Empty(oneStep.WhyLines);
        Assert.StartsWith("1. ", AccessText.Plain(why.WhyLines[0]));
        Assert.All(_detail.Flags.Where(f => f.SectionId is null), f => Assert.Empty(f.WhyLines));
        Assert.False(why.IsWhyShown);

        _store.SetSelection(b1.Id);
        Assert.True(why.IsWhyShown);
        _store.SetSelection(a2.Id);                      // a one-step chain has nothing to list
        Assert.False(why.IsWhyShown);
        Assert.False(oneStep.IsWhyShown);
        _store.SetMultiSelection([b1.Id, a2.Id]);       // exactly one section selected, or nothing is shown
        Assert.False(why.IsWhyShown);
        _store.SetSelection(b1.Id);
        Assert.True(why.IsWhyShown);

        _picture.OpenWatch(_tag.Id);                     // a rebuild keeps showing it for the current selection
        _detail.Refresh();
        Assert.True(Assert.Single(_detail.Flags, f => f.SectionId == b1.Id).IsWhyShown);
    }

    [Fact]
    public void WithSeveralSlates_TheDetailStepsAndListsTheSlateAsText()
    {
        _picture.OpenWatch(_tag.Id);
        Assert.False(_detail.IsStepping);   // off until the box is ticked (Greg, 2026-11-27)
        _detail.StepThroughSlates = true;
        Assert.True(_detail.StepThroughSlates);
        Assert.True(_detail.IsStepping);
        Assert.True(_detail.ShowStepHint);
        Assert.Equal("Slate 1 of 3", _detail.PositionText);
        Assert.False(_detail.CanGoPrevious);
        Assert.True(_detail.CanGoNext);
        Assert.Equal($"A  1{Environment.NewLine}B  2", _detail.SlateText);

        _detail.NextCommand.Execute(null);
        Assert.Equal("Slate 2 of 3", _detail.PositionText);
        Assert.Equal($"A  2{Environment.NewLine}B  1", _detail.SlateText);
        Assert.True(_detail.CanGoPrevious);
    }

    [Fact]
    public void WithOneSlate_NoStepperAndNoPinningHint()
    {
        // pin A 1 and B 2: one slate left
        _picture.OpenWatch(_tag.Id);
        _picture.TogglePin(Get(_fall, "A", "1").Id);
        _picture.TogglePin(Get(_fall, "B", "2").Id);
        Assert.False(_detail.IsStepping);
        Assert.False(_detail.ShowStepHint);
        Assert.Equal(string.Empty, _detail.SlateText);
    }

    [Fact]
    public void ASectionNameInTheDetail_IsALinkThatSelectsTheSection()
    {
        // Same stranded setup: the flag names A 1, which is a link; clicking it selects A 1 (grid and section list)
        var b2 = Get(_fall, "B", "2");
        b2.Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(b2);
        Reload();
        _picture.OpenWatch(_tag.Id);
        var a1 = Get(_fall, "A", "1");

        var flag = Assert.Single(_detail.Flags, f => !f.IsCaution);
        string link = $"[A 1]({AccessText.SectionLinkScheme}{a1.Id})";
        Assert.StartsWith(link, flag.Text);
        Assert.DoesNotContain("](", _detail.HealthLine);   // the one-text line stays plain

        _detail.SelectSectionCommand.Execute($"{AccessText.SectionLinkScheme}{a1.Id}");
        Assert.Equal(new HashSet<string> { a1.Id }, _store.SelectedSectionIds);
    }

    [Fact]
    public void ASectionWithNoTimes_IsAFactUnderTheFold()
    {
        AddSection(_fall, "A", "3", 1, timed: false);
        Reload();
        _picture.OpenWatch(_tag.Id);

        Assert.True(_detail.HasFacts);
        Assert.Equal("Assumes A 3 (no times) fits anywhere.", AccessText.Plain(_detail.Facts.Single()));
        Assert.Equal("More about this watch (1)", _detail.FoldLabel);
        Assert.False(_detail.IsFoldOpen);
        _detail.ToggleFoldCommand.Execute(null);
        Assert.True(_detail.IsFoldOpen);
        Assert.Equal("Less", _detail.FoldLabel);
    }

    [Fact]
    public void ASectionWithoutTheTag_ShowsTheQuietUntaggedLine()
    {
        _picture.OpenWatch(_tag.Id);
        Assert.False(_detail.ShowUntagged);

        AddSection(_fall, "A", "3", 4, tagged: false);
        Reload();
        Assert.True(_detail.ShowUntagged);
        Assert.Equal("Some sections of these courses are not tagged", _detail.UntaggedText);
    }

    [Fact]
    public void MultiCampus_ShowsWhenCampusesSpan_AndIsSavedWithTheWatch()
    {
        foreach (var (course, code, campus) in new[] { ("A", "1", "c1"), ("A", "2", "c2"), ("B", "1", "c1"), ("B", "2", "c2") })
        {
            var s = Get(_fall, course, code);
            s.CampusId = campus;
            _sectionRepo.Update(s);
        }
        Reload();
        _picture.OpenWatch(_tag.Id);
        Assert.True(_detail.ShowMultiCampus);
        Assert.False(_detail.MultiCampus);
        Assert.StartsWith("1 slate", _detail.HealthLine); // one campus rule: only A 2 with B 2

        _detail.MultiCampus = true;
        Assert.True(_envRepo.GetById(_tag.Id)!.WatchMultiCampus);
        Assert.True(_detail.MultiCampus);
        Assert.StartsWith("3 slates", _detail.HealthLine);
        Assert.True(_detail.ShowMultiCampus);

        _detail.MultiCampus = false;
        Assert.False(_envRepo.GetById(_tag.Id)!.WatchMultiCampus);
    }

    [Fact]
    public void ReadOnly_RefusesTheMultiCampusBox_ButTicksStillWork()
    {
        foreach (var (course, code, campus) in new[] { ("A", "1", "c1"), ("A", "2", "c2"), ("B", "1", "c1"), ("B", "2", "c2") })
        {
            var s = Get(_fall, course, code);
            s.CampusId = campus;
            _sectionRepo.Update(s);
        }
        Reload();
        _picture.OpenWatch(_tag.Id);
        _canWrite = false;
        _detail.Refresh();

        Assert.False(_detail.CanChange);
        _detail.MultiCampus = true;
        Assert.False(_envRepo.GetById(_tag.Id)!.WatchMultiCampus);
        Assert.False(_detail.MultiCampus);
        Assert.Equal(AccessText.ReadOnlyTip, _detail.LastErrorMessage);

        _detail.Courses[1].IsTicked = false; // ticks save nothing
        Assert.False(_detail.Courses[1].IsTicked);
    }

    [Fact]
    public void ASecondSemester_GetsASwitch()
    {
        _store.Reload(_sectionRepo, [_fall, _winter]);
        _semesters.Reload(new AcademicYearRepository(_fx.Db), new SemesterRepository(_fx.Db), _yearId,
            new HashSet<string> { _fall, _winter });
        _picture.OpenWatch(_tag.Id);

        Assert.True(_detail.HasSemesterChoice);
        Assert.Equal(["Fall", "Winter"], _detail.SemesterChoices.Select(c => c.Name).OrderBy(n => n).ToArray());
        Assert.Equal("Winter", _detail.SemesterChoices.Single(c => c.IsSelected).Name); // no full slate there
        _detail.SemesterChoices.Single(c => c.Name == "Fall").ChooseCommand.Execute(null);
        Assert.Equal("3 slates · 1 conflict", _detail.HealthLine);
    }

    [Fact]
    public void ACheckedCourse_ShowsTheCheckHeader()
    {
        _picture.OpenCourse("c-A");

        Assert.True(_detail.IsCheckMode);
        Assert.Equal("Check all courses", _detail.BackText);
        Assert.False(_detail.ShowWatching);
        Assert.Equal("A checked on its own: all its sections this term. Not a watch; nothing is saved.", _detail.SourceText);
        Assert.Equal("A", _detail.Name);
        Assert.False(_detail.ShowUntagged);
        Assert.Equal(["A"], _detail.Courses.Select(r => r.Code).ToArray());
        Assert.False(_detail.Courses[0].IsTickEnabled); // the only course: at least one stays ticked
    }

    [Fact]
    public void TheToggler_RefusesTheMultiCampusBoxWithoutTheLock_AndSkipsAnUnchangedValue()
    {
        var toggler = new AccessWatchToggler(_envRepo, _state, _notifier, () => _canWrite);
        int redraws = 0;
        _notifier.GridContentChanged += () => redraws++;

        toggler.SetMultiCampus(_tag.Id, false); // already off: nothing saved, nothing redrawn
        Assert.Equal(0, redraws);

        toggler.SetMultiCampus(_tag.Id, true);
        Assert.True(_envRepo.GetById(_tag.Id)!.WatchMultiCampus);
        Assert.Equal(1, redraws);

        _canWrite = false;
        Assert.Throws<InvalidOperationException>(() => toggler.SetMultiCampus(_tag.Id, false));
        Assert.True(_envRepo.GetById(_tag.Id)!.WatchMultiCampus);
    }

    [Fact]
    public void Back_RaisesTheRequest()
    {
        int back = 0;
        _detail.BackRequested += () => back++;
        _detail.BackCommand.Execute(null);
        Assert.Equal(1, back);
    }

    // ── The pure pieces ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cautions_HardToReach_NamesTheOnlyFit()
    {
        // A 1 (Tue) fits only with B 1 (Wed); A 2 (Mon) fits with all seven of B: A 1 is in 1 of 8 slates
        var sections = new List<Section>
        {
            Sec("A", "1", meetings: At(1, 9, 10)), Sec("A", "2", meetings: At(0, 9, 10)),
            Sec("B", "1", meetings: At(2, 9, 10)),
        };
        for (int i = 2; i <= 7; i++) sections.Add(Sec("B", i.ToString(), meetings: At(1, 9, 10)));
        var inputs = Simple(sections);
        var analysis = AnalyzeAll(inputs);
        Assert.Equal(8, analysis.SlateCount);

        var caution = Assert.Single(Cautions.For(analysis), c => c.Section.Id == "A 1");
        Assert.Equal(CautionKind.HardToReach, caution.Kind);
        Assert.Equal(
            "A 1 fits only with B 1: it conflicts with B 2, B 3, B 4, B 5, B 6 and B 7.",
            AccessText.CautionText(inputs, caution));
    }

    [Fact]
    public void Cautions_HeavyLoad_NamesTheSectionsThatLeanOnIt()
    {
        // A 1 is in 5 of 7 slates; B 1, B 2, B 3 and B 5 fit only with it
        var sections = new List<Section>
        {
            Sec("A", "1", meetings: At(0, 9, 10)), Sec("A", "2", meetings: At(1, 9, 10)), Sec("A", "3", meetings: At(1, 9, 10)),
            Sec("B", "1", meetings: At(1, 9, 10)), Sec("B", "2", meetings: At(1, 9, 10)), Sec("B", "3", meetings: At(1, 9, 10)),
            Sec("B", "4", meetings: At(2, 9, 10)), Sec("B", "5", meetings: At(1, 9, 10)),
        };
        var inputs = Simple(sections);
        var analysis = AnalyzeAll(inputs);
        Assert.Equal(7, analysis.SlateCount);

        // B 4 (in 3 of the 7 slates, in a group of five) carries a heavy load too; this test is about A 1
        var caution = Assert.Single(Cautions.For(analysis), c => c.Section.Id == "A 1");
        Assert.Equal(CautionKind.HeavyLoad, caution.Kind);
        Assert.Equal(
            "A 1 carries a heavy load: B 1, B 2, B 3 and B 5 fit only with it.",
            AccessText.CautionText(inputs, caution));
    }

    [Fact]
    public void Cautions_AreNotJudged_WithoutASlate()
    {
        var inputs = Simple([Sec("A", "1", meetings: At(0, 9, 10)), Sec("B", "1", meetings: At(0, 9, 10))]);
        Assert.Empty(Cautions.For(AnalyzeAll(inputs)));
    }

    [Fact]
    public void CandidatePhrases_SayTheShortestExactThing()
    {
        // A course with one component: 8 sections in the semester
        var sections = Enumerable.Range(1, 8).Select(i => Sec("A", "S" + i, meetings: At(0, 9, 10))).ToList();
        var inputs = Simple(sections);
        string Say(params int[] covered) =>
            AccessText.CandidatePhrases(inputs, "A", covered.Select(i => "A S" + i).ToHashSet()).Single().Text;

        Assert.Equal("all 8 sections", Say(1, 2, 3, 4, 5, 6, 7, 8));
        Assert.Equal("only S3", Say(3));
        Assert.Equal("all but S7, S8", Say(1, 2, 3, 4, 5, 6));
        Assert.Equal("S1, S2, S3", Say(1, 2, 3));
        var four = AccessText.CandidatePhrases(inputs, "A", new[] { 1, 2, 3, 4 }.Select(i => "A S" + i).ToHashSet()).Single();
        Assert.True(four.IsExpandable);
        Assert.Equal("4 of 8 sections", four.Text);
        Assert.Equal("S1, S2, S3, S4 · not in this watch: S5, S6, S7, S8", four.ExpandedLine);
        Assert.Equal("no sections", Say());
    }

    [Fact]
    public void CandidatePhrases_LabelEachComponent()
    {
        var sections = new List<Section>
        {
            Sec("A", "L1", Lecture, At(0, 9, 10)),
            Sec("A", "B1", Lab, At(1, 9, 10)), Sec("A", "B2", Lab, At(2, 9, 10)),
        };
        var inputs = Inputs(sections, [Crs("A", Lecture, Lab)]);
        var phrases = AccessText.CandidatePhrases(inputs, "A", new HashSet<string> { "A L1", "A B1" });

        Assert.Equal(["Lecture: ", "Lab: "], phrases.Select(p => p.Label).ToArray());
        Assert.Equal(["only L1", "only B1"], phrases.Select(p => p.Text).ToArray());

        var none = AccessText.CandidatePhrases(inputs, "A", new HashSet<string> { "A L1" });
        Assert.Equal("none", none[1].Text);
        Assert.True(none[1].IsNone);
    }
}
