using System.Text;
using Moq;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The "delivery type" feature (how a section is taught: F2F, Blended, Online): the optional
/// <see cref="Section.DeliveryTypeId"/>, its section editor field, the Copy Semester option, the section card
/// line, the schedule grid filter, the Delete clean-up (and its refusal while a card is open) and the
/// Sections CSV export. The Import Sections column is covered in the import test classes.
/// Driven with a temp database or Moq stubs; no UI.
/// </summary>
public sealed class DeliveryTypeTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Releases the temp database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    private static SchedulingEnvironmentValue Value(string id, string name) => new() { Id = id, Name = name };

    // ══════════════════════════════════════════════════════════════════════════
    // Persistence
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>A section's delivery type is saved and read back, and a section without one reads back null.</summary>
    [Fact]
    public void Section_DeliveryTypeId_RoundTripsThroughTheDatabase()
    {
        var repo = new SectionRepository(_fx.Db);
        var with = new Section { SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A", DeliveryTypeId = "dt-1" };
        var without = new Section { SemesterId = "sem-1", CourseId = "c-1", SectionCode = "B" };
        repo.Insert(with);
        repo.Insert(without);

        Assert.Equal("dt-1", repo.GetById(with.Id)!.DeliveryTypeId);
        Assert.Null(repo.GetById(without.Id)!.DeliveryTypeId);
    }

    /// <summary>The section list's editing copy carries the delivery type (the editor saves the whole copy back).</summary>
    [Fact]
    public void CloneSection_CarriesTheDeliveryType()
    {
        var clone = SectionListViewModel.CloneSection(new Section { DeliveryTypeId = "dt-1" });

        Assert.Equal("dt-1", clone.DeliveryTypeId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Section editor
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Builds an editor over <paramref name="section"/> offering the given delivery types.</summary>
    private static SectionEditViewModel MakeEditor(
        Section section, IReadOnlyList<SchedulingEnvironmentValue>? deliveryTypes, Func<Section, Task>? onSave = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section, isNew: false, isCopy: false,
            courses: new List<Course> { new() { Id = "c-1", CalendarCode = "c-1" } },
            subjects: new List<Subject>(), instructors: new List<Instructor>(), rooms: new List<Room>(),
            legalStartTimes: new List<LegalStartTime>(), includeSaturday: false, includeSunday: false,
            sectionTypes: new List<SchedulingEnvironmentValue>(), meetingTypes: new List<SchedulingEnvironmentValue>(),
            campuses: new List<Campus>(), allTags: new List<SchedulingEnvironmentValue>(),
            allResources: new List<SchedulingEnvironmentValue>(), allReserves: new List<SchedulingEnvironmentValue>(),
            codePatterns: new List<SectionCodePattern>(), isSectionCodeDuplicate: (_, _) => false,
            onSave: onSave ?? (_ => Task.CompletedTask),
            blockPatternRepository: blockPatternRepo.Object, roomTypes: new List<SchedulingEnvironmentValue>(),
            defaultBlockLength: null, defaultSectionCapacity: null,
            deliveryTypes: deliveryTypes);
    }

    /// <summary>An existing section with course and code set, so the editor opens fully unlocked.</summary>
    private static Section ExistingSection(string? deliveryTypeId = null) =>
        new() { CourseId = "c-1", SectionCode = "A", DeliveryTypeId = deliveryTypeId };

    /// <summary>The dropdown starts with a "(none)" entry (empty id) and lists the defined types after it.</summary>
    [Fact]
    public void Editor_OffersNoneThenTheDefinedTypes()
    {
        var vm = MakeEditor(ExistingSection(), [Value("dt-1", "F2F"), Value("dt-2", "Blended")]);

        Assert.Equal(new[] { "", "dt-1", "dt-2" }, vm.DeliveryTypes.Select(v => v.Id));
        Assert.Equal("(none)", vm.DeliveryTypes[0].Name);
    }

    /// <summary>With no delivery types defined (or none passed) the list is just "(none)".</summary>
    [Fact]
    public void Editor_WithNoDeliveryTypes_OffersOnlyNone()
    {
        Assert.Single(MakeEditor(ExistingSection(), []).DeliveryTypes);
        Assert.Single(MakeEditor(ExistingSection(), null).DeliveryTypes);
    }

    /// <summary>The editor opens on the section's delivery type, or "" when it has none.</summary>
    [Fact]
    public void Editor_LoadsTheSectionsDeliveryType()
    {
        var types = new[] { Value("dt-1", "F2F"), Value("dt-2", "Blended") };

        Assert.Equal("dt-2", MakeEditor(ExistingSection("dt-2"), types).SelectedDeliveryTypeId);
        Assert.Equal("", MakeEditor(ExistingSection(), types).SelectedDeliveryTypeId);
    }

    /// <summary>Apply writes the choice to the section; "(none)" clears it; untouched keeps it.</summary>
    [Theory]
    [InlineData(null, "dt-1", "dt-1")]   // set
    [InlineData("dt-1", "dt-2", "dt-2")] // change
    [InlineData("dt-1", "", null)]       // clear
    [InlineData("dt-1", "dt-1", "dt-1")] // untouched
    public async Task Save_WritesTheSelectedDeliveryType(string? original, string chosen, string? expected)
    {
        Section? saved = null;
        var vm = MakeEditor(ExistingSection(original), [Value("dt-1", "F2F"), Value("dt-2", "Blended")],
            s => { saved = s; return Task.CompletedTask; });

        vm.SelectedDeliveryTypeId = chosen;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal(expected, saved!.DeliveryTypeId);
    }

    /// <summary>Changing only the delivery type counts as an unapplied change; changing it back does not.</summary>
    [Fact]
    public void ChangingTheDeliveryType_IsAnUnappliedChange()
    {
        var vm = MakeEditor(ExistingSection("dt-1"), [Value("dt-1", "F2F"), Value("dt-2", "Blended")]);
        Assert.False(vm.HasUnappliedChanges);

        vm.SelectedDeliveryTypeId = "dt-2";
        Assert.True(vm.HasUnappliedChanges);

        vm.SelectedDeliveryTypeId = "dt-1";
        Assert.False(vm.HasUnappliedChanges);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Copy Semester
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs "Copy and more" from a Fall holding one section with a delivery type into an empty Winter, with the
    /// delivery-type box set as given, and returns the copied section.
    /// </summary>
    private Section CopyWithDeliveryTypeBox(bool include)
    {
        var ayRepo = new AcademicYearRepository(_fx.Db);
        var semRepo = new SemesterRepository(_fx.Db);
        var sectionRepo = new SectionRepository(_fx.Db);
        var ay = new AcademicYear { Id = "ay-1", Name = "2026-2027" };
        ayRepo.Insert(ay);
        var fall = new Semester { Id = "sem-fall", AcademicYearId = ay.Id, Name = "Fall", SortOrder = 1 };
        var winter = new Semester { Id = "sem-winter", AcademicYearId = ay.Id, Name = "Winter", SortOrder = 2 };
        semRepo.Insert(fall);
        semRepo.Insert(winter);
        sectionRepo.Insert(new Section
        {
            SemesterId = fall.Id, CourseId = "c-1", SectionCode = "A", SectionTypeId = "st-1", DeliveryTypeId = "dt-1",
        });

        var vm = new CopySemesterViewModel(ayRepo, semRepo, sectionRepo, _fx.Db, _fx.Lock)
        {
            FromAcademicYear = ay, FromSemester = fall, ToAcademicYear = ay, ToSemester = winter,
            CopyAndMore = true, IncludeDeliveryType = include,
        };
        vm.CopyCommand.Execute(null);

        return Assert.Single(sectionRepo.GetAll(winter.Id));
    }

    /// <summary>The box is ticked by default.</summary>
    [Fact]
    public void CopySemester_IncludeDeliveryType_DefaultsToOn()
    {
        var vm = new CopySemesterViewModel(new AcademicYearRepository(_fx.Db), new SemesterRepository(_fx.Db),
            new SectionRepository(_fx.Db), _fx.Db, _fx.Lock);

        Assert.True(vm.IncludeDeliveryType);
    }

    /// <summary>Ticked: the copy keeps the delivery type.</summary>
    [Fact]
    public void CopySemester_BoxOn_CopiesTheDeliveryType() =>
        Assert.Equal("dt-1", CopyWithDeliveryTypeBox(include: true).DeliveryTypeId);

    /// <summary>Unticked: the copy has none (and, as before, the unticked section type is not copied either).</summary>
    [Fact]
    public void CopySemester_BoxOff_LeavesTheDeliveryTypeOut()
    {
        var copy = CopyWithDeliveryTypeBox(include: false);

        Assert.Null(copy.DeliveryTypeId);
        Assert.Null(copy.SectionTypeId);
    }

    /// <summary>Under "Copy designations only" the sub-options do not apply, so no delivery type is copied.</summary>
    [Fact]
    public void CopySemester_DesignationsOnly_CopiesNoDeliveryType()
    {
        var ayRepo = new AcademicYearRepository(_fx.Db);
        var semRepo = new SemesterRepository(_fx.Db);
        var sectionRepo = new SectionRepository(_fx.Db);
        var ay = new AcademicYear { Id = "ay-1", Name = "2026-2027" };
        ayRepo.Insert(ay);
        var fall = new Semester { Id = "sem-fall", AcademicYearId = ay.Id, Name = "Fall", SortOrder = 1 };
        var winter = new Semester { Id = "sem-winter", AcademicYearId = ay.Id, Name = "Winter", SortOrder = 2 };
        semRepo.Insert(fall);
        semRepo.Insert(winter);
        sectionRepo.Insert(new Section { SemesterId = fall.Id, CourseId = "c-1", SectionCode = "A", DeliveryTypeId = "dt-1" });

        var vm = new CopySemesterViewModel(ayRepo, semRepo, sectionRepo, _fx.Db, _fx.Lock)
        {
            FromAcademicYear = ay, FromSemester = fall, ToAcademicYear = ay, ToSemester = winter,
        };
        vm.CopyCommand.Execute(null);

        Assert.Null(Assert.Single(sectionRepo.GetAll(winter.Id)).DeliveryTypeId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Section card
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Builds a card for a section with the given type ids and the lookups for them.</summary>
    private static SectionListItemViewModel Card(string? sectionTypeId, string? deliveryTypeId, bool passDeliveryLookup = true, int? capacity = null)
    {
        var section = new Section { CourseId = "c-1", SectionCode = "A", SectionTypeId = sectionTypeId, DeliveryTypeId = deliveryTypeId, Capacity = capacity };
        return new SectionListItemViewModel(
            section,
            courseLookup: new Dictionary<string, Course>(),
            instructorLookup: new Dictionary<string, Instructor>(),
            roomLookup: new Dictionary<string, Room>(),
            sectionTypeLookup: new Dictionary<string, SchedulingEnvironmentValue> { ["st-1"] = Value("st-1", "Lecture") },
            campusLookup: new Dictionary<string, Campus>(),
            tagLookup: new Dictionary<string, SchedulingEnvironmentValue>(),
            resourceLookup: new Dictionary<string, SchedulingEnvironmentValue>(),
            reserveLookup: new Dictionary<string, SchedulingEnvironmentValue>(),
            meetingTypeLookup: new Dictionary<string, SchedulingEnvironmentValue>(),
            deliveryTypeLookup: passDeliveryLookup
                ? new Dictionary<string, SchedulingEnvironmentValue> { ["dt-1"] = Value("dt-1", "Blended") }
                : null);
    }

    /// <summary>Both names share one line, joined by a middle dot; either alone when the other is missing; null when neither.</summary>
    [Fact]
    public void Card_TypeLine_JoinsSectionTypeAndDeliveryType()
    {
        Assert.Equal("Lecture · Blended", Card("st-1", "dt-1").TypeLine);
        Assert.Equal("Lecture", Card("st-1", null).TypeLine);
        Assert.Equal("Blended", Card(null, "dt-1").TypeLine);
        Assert.Null(Card(null, null).TypeLine);
    }

    /// <summary>
    /// The summary row shows only the section type while collapsed, and adds the delivery type and capacity once open
    /// (Greg, 2026-11-24: uncluttered collapsed cards). Toggling raises the change so the row updates.
    /// </summary>
    [Fact]
    public void Card_SummaryRow_ShowsDeliveryTypeAndCapacityOnlyWhenOpen()
    {
        var card = Card("st-1", "dt-1", capacity: 30);
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.IsCollapsed = true;
        Assert.Equal("Lecture", card.SummaryTypeText);
        Assert.Null(card.SummaryCapacity);
        Assert.Contains(nameof(SectionListItemViewModel.SummaryTypeText), changed);
        Assert.Contains(nameof(SectionListItemViewModel.SummaryCapacity), changed);

        card.IsCollapsed = false;
        Assert.Equal("Lecture · Blended", card.SummaryTypeText);
        Assert.Equal("30", card.SummaryCapacity);
    }

    /// <summary>The delivery type name resolves from the lookup; a stale id (or no lookup) gives null.</summary>
    [Fact]
    public void Card_DeliveryTypeName_ResolvesFromTheLookup()
    {
        Assert.Equal("Blended", Card(null, "dt-1").DeliveryTypeName);
        Assert.Null(Card(null, "dt-deleted").DeliveryTypeName);
        Assert.Null(Card(null, "dt-1", passDeliveryLookup: false).DeliveryTypeName);
        Assert.Null(Card(null, null).DeliveryTypeName);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Schedule grid filter
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>A section with a delivery type and one Monday meeting.</summary>
    private static Section GridSection(string id, string? deliveryTypeId) => new()
    {
        Id = id, CourseId = "c1", SectionCode = id, SemesterId = "sem1", DeliveryTypeId = deliveryTypeId,
        Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
    };

    /// <summary>A snapshot with every dimension inactive except the given delivery types.</summary>
    private static FilterSnapshot DeliverySnap(params string[] deliveryTypeIds) => new(
        new(), new(), new(), new(), new(), new(), new(), new(), new(),
        false, false, false, false, null, null,
        DeliveryTypeIds: deliveryTypeIds.ToHashSet());

    /// <summary>Lookups holding only the sections and a course.</summary>
    private static GridLookups GridLookupsFor(params Section[] sections) => new(
        Sections: sections,
        Courses: new Dictionary<string, Course> { ["c1"] = new() { Id = "c1", CalendarCode = "HIST101" } },
        ActiveCourses: new Dictionary<string, Course>(),
        Instructors: new Dictionary<string, Instructor>(), ActiveInstructors: new Dictionary<string, Instructor>(),
        Rooms: new Dictionary<string, Room>(), Subjects: new Dictionary<string, Subject>(),
        Campuses: new Dictionary<string, Campus>(),
        SectionTypes: new Dictionary<string, SchedulingEnvironmentValue>(),
        Tags: new Dictionary<string, SchedulingEnvironmentValue>(),
        MeetingTypes: new Dictionary<string, SchedulingEnvironmentValue>(),
        Levels: new Dictionary<string, string>(),
        SemesterIdToName: new Dictionary<string, string>(), SemesterIdToColor: new Dictionary<string, string>());

    /// <summary>The ids of the sections that survive the filter.</summary>
    private static string[] Shown(FilterSnapshot snap, params Section[] sections) =>
        ScheduleGridViewModel.BuildFilteredBlocks(sections, snap, GridLookupsFor(sections), [])
            .OfType<SectionMeetingBlock>().Select(b => b.SectionId).Distinct().OrderBy(i => i).ToArray();

    /// <summary>No delivery type ticked: every section shows, whether or not it has one.</summary>
    [Fact]
    public void Filter_NothingTicked_ShowsEverySection()
    {
        Assert.False(DeliverySnap().FilterDeliveryType);
        Assert.Equal(new[] { "a", "b" }, Shown(DeliverySnap(), GridSection("a", "dt-1"), GridSection("b", null)));
    }

    /// <summary>A ticked delivery type shows only the sections that have it; sections with none are hidden, like section types.</summary>
    [Fact]
    public void Filter_Ticked_ShowsOnlyMatchingSections_AndHidesThoseWithNone()
    {
        var snap = DeliverySnap("dt-1");
        Assert.True(snap.FilterDeliveryType);

        Assert.Equal(new[] { "a" },
            Shown(snap, GridSection("a", "dt-1"), GridSection("b", "dt-2"), GridSection("c", null)));
    }

    /// <summary>Two ticked types show sections with either.</summary>
    [Fact]
    public void Filter_TwoTicked_ShowsSectionsWithEither() =>
        Assert.Equal(new[] { "a", "b" },
            Shown(DeliverySnap("dt-1", "dt-2"), GridSection("a", "dt-1"), GridSection("b", "dt-2"), GridSection("c", null)));

    /// <summary>The filter view model lists the delivery types, ticks feed the selected ids, and the summary and Clear know them.</summary>
    [Fact]
    public void FilterViewModel_ListsTicksSummarisesAndClearsDeliveryTypes()
    {
        var vm = new GridFilterViewModel();
        vm.PopulateOptions(
            new Dictionary<string, Instructor>(), new Dictionary<string, Room>(), new Dictionary<string, Subject>(),
            new Dictionary<string, Campus>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            new Dictionary<string, string>(), new Dictionary<string, Course>(),
            new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["dt-1"] = Value("dt-1", "F2F"), ["dt-2"] = Value("dt-2", "Blended"),
            });

        Assert.Equal(new[] { "Blended", "F2F" }, vm.DeliveryTypes.Select(i => i.Name).OrderBy(n => n));
        Assert.Empty(vm.SelectedDeliveryTypeIds);

        vm.DeliveryTypes.Single(i => i.Id == "dt-2").IsSelected = true;

        Assert.Equal(new[] { "dt-2" }, vm.SelectedDeliveryTypeIds);
        Assert.True(vm.HasRegularFilter);
        Assert.Contains("Delivery: Blended", vm.ActiveSummary);

        vm.ClearFilters();

        Assert.Empty(vm.SelectedDeliveryTypeIds);
        Assert.False(vm.HasRegularFilter);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Delete (Scheduling Environment list)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Answers the delete confirmation and records the questions asked.</summary>
    private sealed class FakeDialog(bool answer) : IDialogService
    {
        /// <summary>The confirmation texts shown.</summary>
        public List<string> Confirmations { get; } = [];

        public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        {
            Confirmations.Add(message);
            return Task.FromResult(answer);
        }

        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>The repositories and the environment-list view model a Delete test needs.</summary>
    private sealed record DeleteRig(
        SectionRepository Sections, SchedulingEnvironmentRepository Env, SectionListViewModel SectionList,
        SchedulingEnvironmentListViewModel List, FakeDialog Dialog);

    /// <summary>Stores an academic year and semester ("sem-1"): sections of a semester that does not exist are orphans the repositories skip.</summary>
    private void StoreSemester()
    {
        new AcademicYearRepository(_fx.Db).Insert(new AcademicYear { Id = "ay-1", Name = "2026-2027" });
        new SemesterRepository(_fx.Db).Insert(new Semester { Id = "sem-1", AcademicYearId = "ay-1", Name = "Fall", SortOrder = 1 });
    }

    /// <summary>Builds the real Section List and a Delivery Type list over the fixture database.</summary>
    private DeleteRig NewDeleteRig(string type = SchedulingEnvironmentTypes.DeliveryType, string name = "Delivery Type")
    {
        var db = _fx.Db;
        var sections = new SectionRepository(db);
        var courses = new CourseRepository(db);
        var subjects = new SubjectRepository(db);
        var instructors = new InstructorRepository(db);
        var env = new SchedulingEnvironmentRepository(db);
        var semesterContext = new SemesterContext();
        var sectionStore = new SectionStore();
        var dialog = new FakeDialog(answer: true);
        var sectionList = new SectionListViewModel(sections, courses, subjects, instructors,
            new RoomRepository(db), new LegalStartTimeRepository(db), new SemesterRepository(db),
            new BlockPatternRepository(db), new SectionCodePatternRepository(db), semesterContext, sectionStore,
            env, new CampusRepository(db), new MeetingRepository(db), dialog, _fx.Lock,
            FocusTestSupport.AllFocus(semesterContext, sectionStore),
            FocusTestSupport.NoColours(courses, subjects, sectionStore), new SectionMarks());
        var list = new SchedulingEnvironmentListViewModel(type, name, env, sections, courses, instructors,
            db, sectionList, dialog, _fx.Lock);
        return new DeleteRig(sections, env, sectionList, list, dialog);
    }

    /// <summary>Deleting a delivery type removes it and clears it from every section that had it; other sections are untouched.</summary>
    [Fact]
    public async Task Delete_DeliveryType_ClearsItFromSections()
    {
        StoreSemester();
        var rig = NewDeleteRig();
        rig.Env.Insert(SchedulingEnvironmentTypes.DeliveryType, Value("dt-1", "F2F"));
        rig.Env.Insert(SchedulingEnvironmentTypes.DeliveryType, Value("dt-2", "Blended"));
        var a = new Section { SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A", DeliveryTypeId = "dt-1", SectionTypeId = "st-1" };
        var b = new Section { SemesterId = "sem-1", CourseId = "c-1", SectionCode = "B", DeliveryTypeId = "dt-2" };
        rig.Sections.Insert(a);
        rig.Sections.Insert(b);
        rig.List.Load();
        rig.List.SelectedItem = rig.List.Items.Single(v => v.Id == "dt-1");

        await rig.List.DeleteCommand.ExecuteAsync(null);

        Assert.Null(rig.List.LastErrorMessage);
        Assert.Null(rig.Env.GetById("dt-1"));
        Assert.Null(rig.Sections.GetById(a.Id)!.DeliveryTypeId);
        Assert.Equal("st-1", rig.Sections.GetById(a.Id)!.SectionTypeId);   // only the delivery type is cleared
        Assert.Equal("dt-2", rig.Sections.GetById(b.Id)!.DeliveryTypeId);
        Assert.Contains("all sections in all semesters that reference it", Assert.Single(rig.Dialog.Confirmations));
    }

    /// <summary>
    /// With a section card open, Delete refuses with the shared "finish editing" message before asking anything,
    /// and changes nothing (the delete would reload the section list and discard what was typed). Applies to
    /// every type, so the same is checked on a section-type list.
    /// </summary>
    [Theory]
    [InlineData(SchedulingEnvironmentTypes.DeliveryType)]
    [InlineData(SchedulingEnvironmentTypes.SectionType)]
    public async Task Delete_WithAnOpenCard_RefusesAndChangesNothing(string type)
    {
        StoreSemester();
        var rig = NewDeleteRig(type, "Type");
        rig.Env.Insert(type, Value("v-1", "Thing"));
        var section = new Section { SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A", DeliveryTypeId = "v-1", SectionTypeId = "v-1" };
        rig.Sections.Insert(section);
        rig.List.Load();
        rig.List.SelectedItem = rig.List.Items.Single();

        // Open a card: the Section List reports one while its editor is set ("a new section" when no card is expanded).
        rig.SectionList.EditVm = MakeEditor(ExistingSection(), null);
        Assert.NotNull(rig.SectionList.OpenCardHeading);

        await rig.List.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(OpenCardMessages.FinishFirst("a new section"), rig.List.LastErrorMessage);
        Assert.Empty(rig.Dialog.Confirmations);                 // nothing was asked
        Assert.NotNull(rig.Env.GetById("v-1"));                 // not deleted
        Assert.Equal("v-1", rig.Sections.GetById(section.Id)!.DeliveryTypeId);
        Assert.Equal("v-1", rig.Sections.GetById(section.Id)!.SectionTypeId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sections CSV export
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>The export has a DeliveryType column right after SectionType, filled with the type's name.</summary>
    [Fact]
    public async Task SectionsCsv_HasADeliveryTypeColumnAfterSectionType()
    {
        var ay = new AcademicYear { Id = "ay-1", Name = "2026-2027" };
        var semester = new Semester { Id = "sem-fall", AcademicYearId = ay.Id, Name = "Fall", SortOrder = 1 };
        var section = new Section
        {
            Id = "s-1", SemesterId = semester.Id, CourseId = "c-1", SectionCode = "A",
            SectionTypeId = "st-1", DeliveryTypeId = "dt-1",
            Schedule =
            [
                new SectionDaySchedule { Day = 1, StartMinutes = 510, DurationMinutes = 90 },
                new SectionDaySchedule { Day = 3, StartMinutes = 510, DurationMinutes = 90 },
            ],
        };
        var bare = new Section { Id = "s-2", SemesterId = semester.Id, CourseId = "c-1", SectionCode = "B" };

        var sectionRepo = new Mock<ISectionRepository>();
        sectionRepo.Setup(r => r.GetAll(semester.Id)).Returns([section, bare]);
        var courseRepo = new Mock<ICourseRepository>();
        courseRepo.Setup(r => r.GetAll()).Returns([new Course { Id = "c-1", CalendarCode = "HIST101", Title = "Intro" }]);
        var instructorRepo = new Mock<IInstructorRepository>();
        instructorRepo.Setup(r => r.GetAll()).Returns([]);
        var roomRepo = new Mock<IRoomRepository>();
        roomRepo.Setup(r => r.GetAll()).Returns([]);
        var semesterRepo = new Mock<ISemesterRepository>();
        semesterRepo.Setup(r => r.GetAll()).Returns([semester]);
        var envRepo = new Mock<ISchedulingEnvironmentRepository>();
        envRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns([]);
        envRepo.Setup(r => r.GetAll(SchedulingEnvironmentTypes.SectionType)).Returns([Value("st-1", "Lecture")]);
        envRepo.Setup(r => r.GetAll(SchedulingEnvironmentTypes.DeliveryType)).Returns([Value("dt-1", "Blended")]);
        var campusRepo = new Mock<ICampusRepository>();
        campusRepo.Setup(r => r.GetAll()).Returns([]);

        var semesterContext = new SemesterContext { SelectedAcademicYear = ay };
        var service = new BackupService(new Mock<IDatabaseContext>().Object, semesterContext, sectionRepo.Object,
            courseRepo.Object, instructorRepo.Object, roomRepo.Object, semesterRepo.Object, envRepo.Object,
            campusRepo.Object, new RecordingAppLogger());

        var path = Path.Combine(Path.GetTempPath(), $"deliverytype_{Guid.NewGuid():N}.csv");
        try
        {
            var method = typeof(BackupService).GetMethod("WriteSectionsCsvAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task<bool>)method.Invoke(service, [path])!;

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var header = lines[0].TrimStart('﻿').Split(',');
            int sectionType = Array.IndexOf(header, "SectionType");
            Assert.True(sectionType >= 0);
            Assert.Equal("DeliveryType", header[sectionType + 1]);

            // First meeting row of section A carries the section-level cells; its continuation row leaves them blank.
            var first = lines[1].Split(',');
            Assert.Equal("Lecture", first[sectionType]);
            Assert.Equal("Blended", first[sectionType + 1]);
            var continuation = lines[2].Split(',');
            Assert.Equal("", continuation[sectionType + 1]);
            Assert.Equal(header.Length, continuation.Length);
            // Section B has none.
            Assert.Equal("", lines[3].Split(',')[sectionType + 1]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
