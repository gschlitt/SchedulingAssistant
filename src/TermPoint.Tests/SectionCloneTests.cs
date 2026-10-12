using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Moq;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Guards against fields being silently lost when a section is edited and Applied.
/// <para>
/// The section list gives the inline editor a <i>clone</i> of the section
/// (<see cref="SectionListViewModel.CloneSection"/>), the editor seeds its controls from that
/// clone, and Save writes the whole clone back to the database. Any persisted field the clone
/// omits is therefore reset to its default on every Apply, with no error and no visible cue.
/// This has happened twice (<c>Flag</c> and each meeting's <c>RoomTypeId</c>), so these tests
/// make a third occurrence fail loudly:
/// </para>
/// <list type="bullet">
///   <item>A reflection-based test that fails when <c>Section</c> (or any nested element type)
///         gains a property that the sample does not populate or the clone does not copy.</item>
///   <item>An end-to-end test that opens the editor on a clone, presses Apply, and checks that a
///         meeting's room type survives.</item>
///   <item>Spec item 3 (unknown JSON fields stored by a newer app version): the reflection guard
///         also requires <see cref="PersistedRecord.ExtensionData"/> to be populated and copied on
///         the section and every nested element, and end-to-end tests check that the editor's
///         Save, which rebuilds the meetings, assignments and reserves, hands each rebuilt record
///         its own original's unknown fields.</item>
/// </list>
/// Driven headlessly with Moq repository stubs, mirroring <c>SectionCapacityTests</c> — no
/// database or UI.
/// </summary>
public class SectionCloneTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a section in which <b>every</b> persisted property, on <see cref="Section"/> and on
    /// each nested element, holds a non-default value. Two elements are used per list so that
    /// ordering and per-element copying are exercised. When a property is added to one of these
    /// types, <see cref="CloneSection_CopiesEveryPersistedField"/> fails until it is populated here
    /// <i>and</i> copied by <see cref="SectionListViewModel.CloneSection"/>.
    /// </summary>
    /// <remarks>
    /// "Persisted property" includes <see cref="PersistedRecord.ExtensionData"/> (the unknown JSON
    /// fields a newer app version stored), which is populated on the section <b>and on every nested
    /// element</b> with an <c>origin</c> marker unique to that element, so a clone or editor that
    /// loses, shares or crosses over the unknown fields of any one of them is detected.
    /// </remarks>
    private static Section MakeFullySetSection() => new()
    {
        Id            = "sec-1",
        SemesterId    = "sem-1",
        CourseId      = "course-1",
        SectionCode   = "A1",
        Notes         = "Some notes",
        SectionTypeId = "type-lecture",
        CampusId      = "campus-1",
        Level         = "200",
        Capacity      = 42,
        Flag          = SectionFlag.Blue,
        TagIds        = ["tag-1", "tag-2"],
        ResourceIds   = ["res-1", "res-2"],
        ExtensionData = FutureFields.Make("section"),
        InstructorAssignments =
        [
            new InstructorAssignment { InstructorId = "inst-1", Workload = 0.5m, ExtensionData = FutureFields.Make("assignment-inst-1") },
            new InstructorAssignment { InstructorId = "inst-2", Workload = 1.5m, ExtensionData = FutureFields.Make("assignment-inst-2") },
        ],
        Reserves =
        [
            new SectionReserve { ReserveId = "rsv-1", Code = 7, ExtensionData = FutureFields.Make("reserve-rsv-1") },
            new SectionReserve { ReserveId = "rsv-2", Code = 9, ExtensionData = FutureFields.Make("reserve-rsv-2") },
        ],
        Schedule =
        [
            new SectionDaySchedule
            {
                Day = 1, StartMinutes = 510, DurationMinutes = 90,
                MeetingTypeId = "mt-lecture", RoomId = "room-1", RoomTypeId = "rt-lab", Frequency = "odd",
                ExtensionData = FutureFields.Make("meeting-0"),
            },
            new SectionDaySchedule
            {
                Day = 3, StartMinutes = 780, DurationMinutes = 120,
                MeetingTypeId = "mt-lab", RoomId = "room-2", RoomTypeId = SectionDaySchedule.RemoteRoomTypeId,
                Frequency = "1,6,7",
                ExtensionData = FutureFields.Make("meeting-1"),
            },
        ],
    };

    /// <summary>
    /// The public instance properties of <paramref name="type"/> that are persisted: those with a
    /// public setter that are not marked <see cref="JsonIgnoreAttribute"/>. Get-only members
    /// (such as <c>EndMinutes</c> and <c>IsRemote</c>) are derived, so they are excluded.
    /// </summary>
    private static IEnumerable<PropertyInfo> PersistedProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true }
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    /// <summary>
    /// True when <paramref name="value"/> is "populated": not null, not a blank string, not an
    /// empty collection, and not the default value of its own type (0, false, the first enum
    /// member, ...). A dictionary, such as <see cref="PersistedRecord.ExtensionData"/>, is a
    /// collection of its entries, so it counts as populated when it has at least one.
    /// </summary>
    private static bool IsPopulated(object? value) => value switch
    {
        null           => false,
        string s       => !string.IsNullOrWhiteSpace(s),
        IEnumerable e  => e.Cast<object?>().Any(),   // also covers non-empty dictionaries
        // Value types (numbers, enums, bools): populated unless equal to default(T). Any other
        // object reference that is non-null counts as populated.
        _              => !value.GetType().IsValueType
                          || !value.Equals(Activator.CreateInstance(value.GetType())),
    };

    /// <summary>
    /// Asserts that every persisted property of <paramref name="instance"/> is populated, then
    /// does the same, recursively, for every element of any collection property. Records each
    /// type visited in <paramref name="visited"/> so the caller can confirm the child types were
    /// really reached.
    /// </summary>
    /// <param name="instance">The object to check.</param>
    /// <param name="path">Where the object sits in the sample, for failure messages.</param>
    /// <param name="visited">Receives the type of every object checked.</param>
    private static void AssertFullyPopulated(object instance, string path, ISet<Type> visited)
    {
        var type = instance.GetType();
        visited.Add(type);

        foreach (var property in PersistedProperties(type))
        {
            var value = property.GetValue(instance);

            Assert.True(IsPopulated(value),
                $"{type.Name}.{property.Name} is null/empty/default in the sample section (at {path}). " +
                $"Populate it in MakeFullySetSection() with a non-default value, and make sure " +
                $"SectionListViewModel.CloneSection copies it. Otherwise the editor will silently " +
                $"reset it on every Apply.");

            // Recurse into collection elements that are themselves objects (not strings).
            if (value is IEnumerable elements and not string)
            {
                int i = 0;
                foreach (var element in elements)
                {
                    if (element is not null && element.GetType().IsClass && element is not string)
                        AssertFullyPopulated(element, $"{path}.{property.Name}[{i}]", visited);
                    i++;
                }
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="SectionEditViewModel"/> over <paramref name="section"/> with one
    /// course ("c-1") and the given room types; every other dependency is an empty stub.
    /// </summary>
    /// <param name="section">The section to edit (normally a clone from <c>CloneSection</c>).</param>
    /// <param name="roomTypes">Room-type values offered by the meeting editors' Room Type dropdown.</param>
    /// <param name="onSave">Capture callback invoked by Save.</param>
    /// <param name="instructors">
    /// Instructors offered by the editor's multi-select. A section's assignment only becomes an
    /// editor row (and so survives Save) when its instructor is in this list.
    /// </param>
    /// <param name="allReserves">Reserve values offered by the editor, with the same rule.</param>
    private static SectionEditViewModel MakeEditorVm(
        Section section,
        IReadOnlyList<SchedulingEnvironmentValue> roomTypes,
        Func<Section, Task> onSave,
        IReadOnlyList<Instructor>? instructors = null,
        IReadOnlyList<SchedulingEnvironmentValue>? allReserves = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section,
            isNew:             false,
            isCopy:            false,
            courses:           new List<Course> { new() { Id = "c-1", CalendarCode = "c-1" } },
            subjects:          new List<Subject>(),
            instructors:       instructors ?? new List<Instructor>(),
            rooms:             new List<Room>(),
            legalStartTimes:   new List<LegalStartTime>(),
            includeSaturday:   false,
            includeSunday:     false,
            sectionTypes:      new List<SchedulingEnvironmentValue>(),
            meetingTypes:      new List<SchedulingEnvironmentValue>(),
            campuses:          new List<Campus>(),
            allTags:           new List<SchedulingEnvironmentValue>(),
            allResources:      new List<SchedulingEnvironmentValue>(),
            allReserves:       allReserves ?? new List<SchedulingEnvironmentValue>(),
            codePatterns:      new List<SectionCodePattern>(),
            isSectionCodeDuplicate: (_, _) => false,
            onSave:            onSave,
            blockPatternRepository: blockPatternRepo.Object,
            roomTypes:         roomTypes,
            defaultBlockLength: null,
            defaultSectionCapacity: null);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CloneSection — every persisted field survives the copy
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CloneSection_CopiesEveryPersistedField()
    {
        var source = MakeFullySetSection();

        // 1. The sample must really populate every persisted property, on Section and on every
        //    nested element type. Otherwise a field the clone drops would compare equal (both
        //    sides default) and slip through.
        var visited = new HashSet<Type>();
        AssertFullyPopulated(source, nameof(Section), visited);
        Assert.Contains(typeof(Section), visited);
        Assert.Contains(typeof(InstructorAssignment), visited);
        Assert.Contains(typeof(SectionDaySchedule), visited);
        Assert.Contains(typeof(SectionReserve), visited);

        // 2. Clone, and compare what would be persisted.
        var clone = SectionListViewModel.CloneSection(source);

        // Level is deliberately NOT cloned: SectionEditViewModel.Save re-derives it from the
        // course on every Apply, so the editor never needs the old value. Align it so the
        // comparison covers everything else.
        clone.Level = source.Level;

        var expectedJson = JsonHelpers.Serialize(source);
        var actualJson   = JsonHelpers.Serialize(clone);
        Assert.True(expectedJson == actualJson,
            "CloneSection dropped or altered a persisted field, which the editor would then silently " +
            "reset on Apply. Copy the missing field in SectionListViewModel.CloneSection." +
            $"{Environment.NewLine}expected: {expectedJson}{Environment.NewLine}actual:   {actualJson}");

        // 3. It must be a deep copy: Cancel relies on the list's own section being untouched.
        Assert.NotSame(source, clone);
        Assert.NotSame(source.Schedule, clone.Schedule);
        Assert.NotSame(source.InstructorAssignments, clone.InstructorAssignments);
        Assert.NotSame(source.TagIds, clone.TagIds);
        Assert.NotSame(source.ResourceIds, clone.ResourceIds);
        Assert.NotSame(source.Reserves, clone.Reserves);

        Assert.Equal(source.Schedule.Count, clone.Schedule.Count);
        for (int i = 0; i < source.Schedule.Count; i++)
            Assert.NotSame(source.Schedule[i], clone.Schedule[i]);

        Assert.Equal(source.InstructorAssignments.Count, clone.InstructorAssignments.Count);
        for (int i = 0; i < source.InstructorAssignments.Count; i++)
            Assert.NotSame(source.InstructorAssignments[i], clone.InstructorAssignments[i]);

        Assert.Equal(source.Reserves.Count, clone.Reserves.Count);
        for (int i = 0; i < source.Reserves.Count; i++)
            Assert.NotSame(source.Reserves[i], clone.Reserves[i]);

        // 4. The unknown JSON fields are copied into NEW dictionaries at every level: equal content
        //    (checked through the JSON comparison above) but no shared mutable state, so editing the
        //    clone's extension data can never leak into the list's own section.
        AssertExtensionDataIsIndependent(source, clone);
        for (int i = 0; i < source.Schedule.Count; i++)
            AssertExtensionDataIsIndependent(source.Schedule[i], clone.Schedule[i]);
        for (int i = 0; i < source.InstructorAssignments.Count; i++)
            AssertExtensionDataIsIndependent(source.InstructorAssignments[i], clone.InstructorAssignments[i]);
        for (int i = 0; i < source.Reserves.Count; i++)
            AssertExtensionDataIsIndependent(source.Reserves[i], clone.Reserves[i]);
    }

    /// <summary>
    /// Asserts that the clone holds its own copy of the original's unknown JSON fields: the same
    /// entries, in a different dictionary instance.
    /// </summary>
    private static void AssertExtensionDataIsIndependent(PersistedRecord original, PersistedRecord clone)
    {
        Assert.NotNull(original.ExtensionData);
        Assert.NotNull(clone.ExtensionData);
        Assert.NotSame(original.ExtensionData, clone.ExtensionData);
        Assert.Equal(original.ExtensionData!.Keys, clone.ExtensionData!.Keys);
    }

    [Theory]
    [InlineData(SectionFlag.None)]
    [InlineData(SectionFlag.Red)]
    [InlineData(SectionFlag.Blue)]
    [InlineData(SectionFlag.Green)]
    public void CloneSection_CarriesTheFlag(SectionFlag flag)
    {
        // The editor's Flag dropdown is seeded from the clone, and Apply writes it back.
        var source = new Section { CourseId = "c-1", SectionCode = "A", Flag = flag };

        var clone = SectionListViewModel.CloneSection(source);

        Assert.NotSame(source, clone);
        Assert.Equal(flag, clone.Flag);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Editor Apply — end to end through the clone
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(SectionDaySchedule.RemoteRoomTypeId)]   // the "Remote" marker
    [InlineData("rt-lab")]                              // an ordinary room-type id
    public async Task EditorApply_KeepsMeetingRoomType(string roomTypeId)
    {
        // An existing section with one complete meeting that has a room type.
        var source = new Section
        {
            CourseId    = "c-1",
            SectionCode = "A",
            Schedule =
            [
                new SectionDaySchedule
                {
                    Day = 2, StartMinutes = 510, DurationMinutes = 90, RoomTypeId = roomTypeId,
                },
            ],
        };

        // The editor adds "Remote" to the Room Type dropdown itself; an ordinary id must be
        // among the room types it is given, or the dropdown could not show it.
        var roomTypes = roomTypeId == SectionDaySchedule.RemoteRoomTypeId
            ? new List<SchedulingEnvironmentValue>()
            : new List<SchedulingEnvironmentValue> { new() { Id = roomTypeId, Name = "Lab" } };

        // Open the editor the way the list does: on a clone of the section.
        Section? saved = null;
        var vm = MakeEditorVm(SectionListViewModel.CloneSection(source), roomTypes,
                              onSave: s => { saved = s; return Task.CompletedTask; });

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);   // Save was accepted (the meeting is complete and valid)
        var meeting = Assert.Single(saved!.Schedule);
        Assert.Equal(roomTypeId, meeting.RoomTypeId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Editor Apply — unknown JSON fields (spec item 3) survive the rebuild
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>The instructors offered to the editor in the unknown-field tests.</summary>
    private static List<Instructor> UnknownFieldInstructors() =>
    [
        new() { Id = "inst-1", LastName = "One",   FirstName = "A" },
        new() { Id = "inst-2", LastName = "Two",   FirstName = "B" },
        new() { Id = "inst-3", LastName = "Three", FirstName = "C" },
    ];

    /// <summary>The reserve values offered to the editor in the unknown-field tests.</summary>
    private static List<SchedulingEnvironmentValue> UnknownFieldReserves() =>
    [
        new() { Id = "rsv-1", Name = "R1" },
        new() { Id = "rsv-2", Name = "R2" },
        new() { Id = "rsv-3", Name = "R3" },
    ];

    /// <summary>
    /// The editor rebuilds a section's meetings, instructor assignments and reserves from its rows
    /// on Save, replacing the objects it was given. This opens the editor on a clone of a section
    /// whose section, meetings, assignments and reserves all carry unknown JSON fields (as if a
    /// newer app version had stored them), changes a known field, Saves, and requires every unknown
    /// field at every level to survive, each on the record it came from.
    /// </summary>
    [Fact]
    public async Task EditorApply_KeepsUnknownFields_AtEveryLevel()
    {
        var source = MakeFullySetSection();
        source.CourseId = "c-1";                       // a course the stub editor offers
        var roomTypes = new List<SchedulingEnvironmentValue> { new() { Id = "rt-lab", Name = "Lab" } };

        Section? saved = null;
        var vm = MakeEditorVm(SectionListViewModel.CloneSection(source), roomTypes,
                              onSave: s => { saved = s; return Task.CompletedTask; },
                              UnknownFieldInstructors(), UnknownFieldReserves());

        vm.Notes = "edited by an older client";        // an ordinary edit of a known field
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("edited by an older client", saved!.Notes);
        Assert.Equal(2, saved.Schedule.Count);
        Assert.Equal(2, saved.InstructorAssignments.Count);
        Assert.Equal(2, saved.Reserves.Count);

        var json = JsonNode.Parse(JsonHelpers.Serialize(saved))!;
        FutureFields.AssertPresent(json, "section", "section");
        FutureFields.AssertPresent(json["schedule"]![0], "meeting 0", "meeting-0");
        FutureFields.AssertPresent(json["schedule"]![1], "meeting 1", "meeting-1");
        foreach (var a in json["instructorAssignments"]!.AsArray())
            FutureFields.AssertPresent(a, $"assignment {a!["instructorId"]}", $"assignment-{a["instructorId"]!.GetValue<string>()}");
        foreach (var r in json["reserves"]!.AsArray())
            FutureFields.AssertPresent(r, $"reserve {r!["reserveId"]}", $"reserve-{r["reserveId"]!.GetValue<string>()}");

        // The editor worked on a clone: the section the list holds is untouched and shares nothing.
        Assert.NotSame(source.Schedule[0].ExtensionData, saved.Schedule[0].ExtensionData);
        Assert.NotSame(source.InstructorAssignments[0].ExtensionData, saved.InstructorAssignments[0].ExtensionData);
        Assert.NotSame(source.Reserves[0].ExtensionData, saved.Reserves[0].ExtensionData);
    }

    /// <summary>
    /// Rows map back to the right original, and only to it: removing the first meeting leaves the
    /// second meeting with ITS unknown fields (not the first's); a meeting, instructor and reserve
    /// added during the edit start with none; an instructor and a reserve that are un-ticked take
    /// theirs with them. This is the "new rows have none, removed rows drop theirs" rule.
    /// </summary>
    [Fact]
    public async Task EditorApply_MapsUnknownFieldsToTheRightRows_AcrossRemovalsAndAdditions()
    {
        var source = MakeFullySetSection();
        source.CourseId = "c-1";
        var roomTypes = new List<SchedulingEnvironmentValue> { new() { Id = "rt-lab", Name = "Lab" } };

        Section? saved = null;
        var vm = MakeEditorVm(SectionListViewModel.CloneSection(source), roomTypes,
                              onSave: s => { saved = s; return Task.CompletedTask; },
                              UnknownFieldInstructors(), UnknownFieldReserves());

        // Meetings: drop the first original, add a brand-new one.
        vm.RemoveMeetingCommand.Execute(vm.Meetings[0]);
        vm.AddMeetingCommand.Execute(null);
        var added = vm.Meetings[^1];
        added.SelectedDay = 2;
        added.StartTimeText = "0900";
        await added.CommitStartTimeCommand.ExecuteAsync(null);
        added.BlockLengthText = "1";
        await added.CommitBlockLengthCommand.ExecuteAsync(null);

        // Instructors: un-tick inst-1, tick the new inst-3.
        vm.InstructorSelections.First(i => i.Value.Id == "inst-1").IsSelected = false;
        vm.InstructorSelections.First(i => i.Value.Id == "inst-3").IsSelected = true;

        // Reserves: un-tick rsv-1, tick the new rsv-3.
        vm.ReserveSelections.First(r => r.Value.Id == "rsv-1").IsSelected = false;
        vm.ReserveSelections.First(r => r.Value.Id == "rsv-3").IsSelected = true;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        var json = JsonNode.Parse(JsonHelpers.Serialize(saved))!;

        var schedule = json["schedule"]!.AsArray();
        Assert.Equal(2, schedule.Count);
        FutureFields.AssertPresent(schedule[0], "surviving original meeting", "meeting-1");   // not meeting-0
        FutureFields.AssertAbsent(schedule[1], "meeting added during the edit");

        var assignments = json["instructorAssignments"]!.AsArray().ToDictionary(a => a!["instructorId"]!.GetValue<string>());
        Assert.Equal(new[] { "inst-2", "inst-3" }, assignments.Keys.OrderBy(k => k));
        FutureFields.AssertPresent(assignments["inst-2"], "kept instructor", "assignment-inst-2");
        FutureFields.AssertAbsent(assignments["inst-3"], "instructor added during the edit");

        var reserves = json["reserves"]!.AsArray().ToDictionary(r => r!["reserveId"]!.GetValue<string>());
        Assert.Equal(new[] { "rsv-2", "rsv-3" }, reserves.Keys.OrderBy(k => k));
        FutureFields.AssertPresent(reserves["rsv-2"], "kept reserve", "reserve-rsv-2");
        FutureFields.AssertAbsent(reserves["rsv-3"], "reserve added during the edit");

        // The section's own unknown fields are unaffected by edits to its children.
        FutureFields.AssertPresent(json, "section", "section");
    }

    /// <summary>
    /// The whole pipeline, with a real database: a row holding unknown fields on the section and on
    /// each of its nested records is loaded by the repository, cloned for the editor, edited and
    /// saved back with <c>Update</c>; the raw <c>data</c> column must still hold every one of them.
    /// </summary>
    [Fact]
    public async Task EditorApply_ThroughTheDatabase_PreservesUnknownFields()
    {
        using var fx = new ExtensionDataDbFixture();
        var repo = new SectionRepository(fx.Db);
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
            InstructorAssignments = [new() { InstructorId = "inst-1", Workload = 1m }],
            Reserves = [new() { ReserveId = "rsv-1", Code = 4 }],
        };
        repo.Insert(section);
        fx.Stamp("Sections", section.Id, "$", "section");
        fx.Stamp("Sections", section.Id, "$.schedule[0]", "meeting-0");
        fx.Stamp("Sections", section.Id, "$.instructorAssignments[0]", "assignment-inst-1");
        fx.Stamp("Sections", section.Id, "$.reserves[0]", "reserve-rsv-1");

        // Open the editor exactly as the list does: repository -> CloneSection -> editor -> Update.
        var loaded = repo.GetById(section.Id)!;
        var vm = MakeEditorVm(SectionListViewModel.CloneSection(loaded), new List<SchedulingEnvironmentValue>(),
                              onSave: s => { repo.Update(s); return Task.CompletedTask; },
                              UnknownFieldInstructors(), UnknownFieldReserves());
        vm.Notes = "edited by an older client";
        await vm.SaveCommand.ExecuteAsync(null);

        var raw = fx.ReadData("Sections", section.Id);
        Assert.Equal("edited by an older client", raw["notes"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "section", "section");
        FutureFields.AssertPresent(raw["schedule"]![0], "meeting 0", "meeting-0");
        FutureFields.AssertPresent(raw["instructorAssignments"]![0], "assignment", "assignment-inst-1");
        FutureFields.AssertPresent(raw["reserves"]![0], "reserve", "reserve-rsv-1");
        FutureFields.AssertNoDuplicateKeys(fx.ReadDataText("Sections", section.Id));
    }
}
