using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the section attention flag as edited in <see cref="SectionEditViewModel"/>
/// (the Flag dropdown beside Notes): the shared option list, loading the section's existing
/// flag, Apply writing the selection back, and Cancel discarding it. That the list's
/// <c>CloneSection</c> carries the flag into the editor is covered in <c>SectionCloneTests</c>.
/// Driven headlessly with Moq repository stubs, mirroring <c>SectionCapacityTests</c> — no
/// database or UI.
/// </summary>
public class SectionFlagTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal course with the given id (also used as its calendar code).</summary>
    private static Course MakeCourse(string id) => new() { Id = id, CalendarCode = id };

    /// <summary>
    /// Builds a <see cref="SectionEditViewModel"/> for <paramref name="section"/> with one course
    /// and otherwise empty stubs. The caller supplies the <paramref name="onSave"/> capture.
    /// </summary>
    private static SectionEditViewModel MakeVm(
        Section section,
        bool isNew = false,
        Func<Section, Task>? onSave = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section,
            isNew,
            isCopy:            false,
            courses:           new List<Course> { MakeCourse("c-1") },
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
            defaultSectionCapacity: null);
    }

    /// <summary>An existing section with a course and code already set, so the editor opens fully unlocked.</summary>
    private static Section MakeExistingSection(SectionFlag flag) => new()
    {
        CourseId    = "c-1",
        SectionCode = "A",
        Flag        = flag,
    };

    // ══════════════════════════════════════════════════════════════════════════
    // FlagOptionVm.CreateOptions — the shared option list
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateOptions_ReturnsNoneRedBlueGreen_InOrder()
    {
        var options = FlagOptionVm.CreateOptions();

        Assert.Equal(
            new[] { SectionFlag.None, SectionFlag.Red, SectionFlag.Blue, SectionFlag.Green },
            options.Select(o => o.Value).ToArray());
        Assert.Equal(
            new[] { "(None)", "Red", "Blue", "Green" },
            options.Select(o => o.Label).ToArray());

        // Only the None option has no icon.
        Assert.False(options[0].HasIcon);
        Assert.All(options.Skip(1), o => Assert.True(o.HasIcon));
    }

    [Fact]
    public void CreateOptions_ReturnsNewInstancesOnEachCall()
    {
        // Brushes are resolved at construction, so each picker must get its own fresh options.
        var first  = FlagOptionVm.CreateOptions();
        var second = FlagOptionVm.CreateOptions();

        Assert.NotSame(first, second);
        for (int i = 0; i < first.Count; i++)
            Assert.NotSame(first[i], second[i]);
    }

    [Fact]
    public void Editor_FlagOptions_MatchTheSharedList()
    {
        var vm = MakeVm(MakeExistingSection(SectionFlag.None));

        Assert.Equal(
            FlagOptionVm.CreateOptions().Select(o => o.Value).ToArray(),
            vm.FlagOptions.Select(o => o.Value).ToArray());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Loading — the editor starts on the section's existing flag
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(SectionFlag.None)]
    [InlineData(SectionFlag.Red)]
    [InlineData(SectionFlag.Blue)]
    [InlineData(SectionFlag.Green)]
    public void Editor_LoadsTheSectionsExistingFlag(SectionFlag flag)
    {
        var vm = MakeVm(MakeExistingSection(flag));

        Assert.Equal(flag, vm.SelectedFlag);
    }

    [Fact]
    public void NewSection_StartsUnflagged()
    {
        var vm = MakeVm(new Section(), isNew: true);

        Assert.Equal(SectionFlag.None, vm.SelectedFlag);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Apply — Save writes the selected flag to the section
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(SectionFlag.None, SectionFlag.Green)]   // set a flag
    [InlineData(SectionFlag.Red,  SectionFlag.Blue)]    // change a flag
    [InlineData(SectionFlag.Red,  SectionFlag.None)]    // clear a flag
    public async Task Save_WritesTheSelectedFlagToTheSection(SectionFlag original, SectionFlag chosen)
    {
        Section? saved = null;
        var section = MakeExistingSection(original);
        var vm = MakeVm(section, onSave: s => { saved = s; return Task.CompletedTask; });

        vm.SelectedFlag = chosen;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal(chosen, saved!.Flag);
        Assert.Equal(chosen, section.Flag);   // the editor edits the section it was given
    }

    [Fact]
    public async Task Save_WithoutTouchingTheDropdown_KeepsTheExistingFlag()
    {
        // Guards against Apply silently clearing a flag the user never touched.
        Section? saved = null;
        var vm = MakeVm(MakeExistingSection(SectionFlag.Red),
                        onSave: s => { saved = s; return Task.CompletedTask; });

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal(SectionFlag.Red, saved!.Flag);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Cancel — discards the change
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cancel_LeavesTheSectionsFlagUnchanged()
    {
        var saveCalled = false;
        var closed = false;
        var section = MakeExistingSection(SectionFlag.Red);
        var vm = MakeVm(section, onSave: _ => { saveCalled = true; return Task.CompletedTask; });
        vm.RequestClose = () => closed = true;

        vm.SelectedFlag = SectionFlag.Green;
        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.False(saveCalled);
        Assert.Equal(SectionFlag.Red, section.Flag);   // the pending choice never reached the section
    }
}
