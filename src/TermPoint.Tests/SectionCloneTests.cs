using System.Collections;
using System.Reflection;
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
        InstructorAssignments =
        [
            new InstructorAssignment { InstructorId = "inst-1", Workload = 0.5m },
            new InstructorAssignment { InstructorId = "inst-2", Workload = 1.5m },
        ],
        Reserves =
        [
            new SectionReserve { ReserveId = "rsv-1", Code = 7 },
            new SectionReserve { ReserveId = "rsv-2", Code = 9 },
        ],
        Schedule =
        [
            new SectionDaySchedule
            {
                Day = 1, StartMinutes = 510, DurationMinutes = 90,
                MeetingTypeId = "mt-lecture", RoomId = "room-1", RoomTypeId = "rt-lab", Frequency = "odd",
            },
            new SectionDaySchedule
            {
                Day = 3, StartMinutes = 780, DurationMinutes = 120,
                MeetingTypeId = "mt-lab", RoomId = "room-2", RoomTypeId = SectionDaySchedule.RemoteRoomTypeId,
                Frequency = "1,6,7",
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
    /// member, ...).
    /// </summary>
    private static bool IsPopulated(object? value) => value switch
    {
        null           => false,
        string s       => !string.IsNullOrWhiteSpace(s),
        IEnumerable e  => e.Cast<object?>().Any(),
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
    private static SectionEditViewModel MakeEditorVm(
        Section section,
        IReadOnlyList<SchedulingEnvironmentValue> roomTypes,
        Func<Section, Task> onSave)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section,
            isNew:             false,
            isCopy:            false,
            courses:           new List<Course> { new() { Id = "c-1", CalendarCode = "c-1" } },
            subjects:          new List<Subject>(),
            instructors:       new List<Instructor>(),
            rooms:             new List<Room>(),
            legalStartTimes:   new List<LegalStartTime>(),
            includeSaturday:   false,
            includeSunday:     false,
            sectionTypes:      new List<SchedulingEnvironmentValue>(),
            meetingTypes:      new List<SchedulingEnvironmentValue>(),
            campuses:          new List<Campus>(),
            allTags:           new List<SchedulingEnvironmentValue>(),
            allResources:      new List<SchedulingEnvironmentValue>(),
            allReserves:       new List<SchedulingEnvironmentValue>(),
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
}
