using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for section capacity behaviour in <see cref="SectionEditViewModel"/>:
/// inheritance seeding (Course.Capacity → app default), copy/edit preservation, and
/// the parse/normalize/persist round-trip. Driven headlessly with Moq repository stubs —
/// no database or UI. Complements the step-gate tests in <c>EditorFlowTests</c>.
/// </summary>
public class SectionCapacityTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Course MakeCourse(string id, int? capacity) =>
        new() { Id = id, CalendarCode = id, Capacity = capacity };

    /// <summary>
    /// Builds a <see cref="SectionEditViewModel"/> with the given courses and app-default
    /// capacity. All other dependencies are empty stubs (mirrors EditorFlowTests).
    /// </summary>
    private static SectionEditViewModel MakeVm(
        Section section,
        bool isNew = true,
        bool isCopy = false,
        IEnumerable<Course>? courses = null,
        int? defaultSectionCapacity = null,
        Func<Section, Task>? onSave = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section,
            isNew,
            isCopy:            isCopy,
            courses:           (courses ?? Enumerable.Empty<Course>()).ToList(),
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
            onSave:            onSave ?? (_ => Task.CompletedTask),
            blockPatternRepository: blockPatternRepo.Object,
            roomTypes:         new List<SchedulingEnvironmentValue>(),
            defaultBlockLength: null,
            defaultSectionCapacity: defaultSectionCapacity);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Inheritance seeding — new section picks up Course.Capacity, else app default
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NewSection_CourseWithCapacity_SeedsFromCourse()
    {
        var course = MakeCourse("c-1", capacity: 30);
        var vm = MakeVm(new Section(), courses: new[] { course }, defaultSectionCapacity: 25);

        vm.SelectedCourseId = course.Id;

        Assert.Equal("30", vm.CapacityText);
    }

    [Fact]
    public void NewSection_CourseWithoutCapacity_SeedsFromAppDefault()
    {
        var course = MakeCourse("c-1", capacity: null);
        var vm = MakeVm(new Section(), courses: new[] { course }, defaultSectionCapacity: 25);

        vm.SelectedCourseId = course.Id;

        Assert.Equal("25", vm.CapacityText);
    }

    [Fact]
    public void NewSection_NoCourseCapacity_NoDefault_SeedsEmpty_NotZero()
    {
        var course = MakeCourse("c-1", capacity: null);
        var vm = MakeVm(new Section(), courses: new[] { course }, defaultSectionCapacity: null);

        vm.SelectedCourseId = course.Id;

        Assert.Equal(string.Empty, vm.CapacityText);   // null means "unknown", never "0"
    }

    [Fact]
    public void NewSection_SwitchingCourse_ReDerivesCapacity()
    {
        var withCap    = MakeCourse("c-1", capacity: 30);
        var withoutCap = MakeCourse("c-2", capacity: null);
        var vm = MakeVm(new Section(), courses: new[] { withCap, withoutCap }, defaultSectionCapacity: 25);

        vm.SelectedCourseId = withCap.Id;
        Assert.Equal("30", vm.CapacityText);

        vm.SelectedCourseId = withoutCap.Id;
        Assert.Equal("25", vm.CapacityText);            // falls back to the app default
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Copy / Edit — existing capacity is preserved, not re-derived
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CopySection_DoesNotOverwriteCapacityOnCourseSelect()
    {
        var course  = MakeCourse("c-1", capacity: 99);
        var section = new Section { Capacity = 70 };
        var vm = MakeVm(section, isNew: true, isCopy: true,
                        courses: new[] { course }, defaultSectionCapacity: 25);

        Assert.Equal("70", vm.CapacityText);            // seeded from the copied section

        vm.SelectedCourseId = course.Id;

        Assert.Equal("70", vm.CapacityText);            // course's 99 must not clobber it
    }

    [Fact]
    public void EditSection_DoesNotReDeriveCapacityOnCourseChange()
    {
        var course  = MakeCourse("c-1", capacity: 99);
        var section = new Section { Capacity = 50 };
        var vm = MakeVm(section, isNew: false, isCopy: false,
                        courses: new[] { course }, defaultSectionCapacity: 25);

        Assert.Equal("50", vm.CapacityText);

        vm.SelectedCourseId = course.Id;

        Assert.Equal("50", vm.CapacityText);            // edits keep the section's own value
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Parse / normalize / persist — ParsedCapacity round-trips through Save
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("45", 45)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("-5", null)]     // negative is treated as unspecified
    [InlineData("abc", null)]    // non-numeric is treated as unspecified
    public async Task Save_NormalizesCapacityText(string capacityText, int? expected)
    {
        Section? saved = null;
        var course = MakeCourse("c-1", capacity: null);
        var vm = MakeVm(new Section(), courses: new[] { course },
                        onSave: s => { saved = s; return Task.CompletedTask; });

        // Unlock the editor through the step-gate, then set the capacity under test.
        vm.SelectedCourseId = course.Id;
        vm.SectionCode      = "A";
        vm.CommitSectionCode();
        vm.CapacityText     = capacityText;

        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        await Task.Yield();

        Assert.NotNull(saved);
        Assert.Equal(expected, saved!.Capacity);
    }
}
