using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Each watch in the Access panel carries hover text (<see cref="ProgramWatchItemViewModel.Details"/>) naming what
/// it watches, so the user does not have to open the editor to see a watch's choices.
/// </summary>
public sealed class AccessPanelWatchDetailsTests
{
    /// <summary>
    /// Builds a read-only panel (so watches stay in memory and no repository save is needed) with sem-1 selected,
    /// tags "Core" (t1) and "BSc" (t2), active course BIOL101 (c1) and inactive course BIOL099 (old).
    /// </summary>
    private static AccessPanelViewModel NewPanel()
    {
        var year = new AcademicYear { Id = "ay-1", Name = "2026-2027" };
        var ayRepo = new Mock<IAcademicYearRepository>();
        ayRepo.Setup(r => r.GetAll()).Returns(new List<AcademicYear> { year });
        var semRepo = new Mock<ISemesterRepository>();
        semRepo.Setup(r => r.GetAll()).Returns(new List<Semester>
        {
            new() { Id = "sem-1", AcademicYearId = "ay-1", Name = "Fall", SortOrder = 0 },
        });
        var semesters = new SemesterContext();
        semesters.Reload(ayRepo.Object, semRepo.Object, "ay-1", new HashSet<string> { "sem-1" });

        var watchRepo = new Mock<IProgramWatchRepository>();
        watchRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<ProgramWatch>());

        var envRepo = new Mock<ISchedulingEnvironmentRepository>();
        envRepo.Setup(r => r.GetAll(SchedulingEnvironmentTypes.Tag)).Returns(new List<SchedulingEnvironmentValue>
        {
            new() { Id = "t1", Name = "Core" },
            new() { Id = "t2", Name = "BSc" },
        });

        var courseRepo = new Mock<ICourseRepository>();
        courseRepo.Setup(r => r.GetById("c1")).Returns(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        courseRepo.Setup(r => r.GetById("old")).Returns(new Course { Id = "old", CalendarCode = "BIOL099", IsActive = false });

        return new AccessPanelViewModel(watchRepo.Object, semesters, new GridChangeNotifier(),
            envRepo.Object, courseRepo.Object, () => false);
    }

    /// <summary>A tag watch names its tags (all of) and levels (any of), one line each; a missing tag is marked.</summary>
    [Fact]
    public void TagWatch_Details_NameTagsAndLevels()
    {
        var panel = NewPanel();

        panel.CreateWatch(new ProgramWatch
        {
            Name = "w", Mode = ProgramWatchMode.Tag, TagIds = ["t1", "t2", "gone"], LevelIds = ["300", "400"]
        });

        Assert.Equal(
            $"Tags (all of): Core, BSc, (deleted tag){Environment.NewLine}Levels (any of): 300, 400",
            Assert.Single(panel.Watches).Details);
    }

    /// <summary>A levels-only tag watch shows just the levels line.</summary>
    [Fact]
    public void TagWatch_LevelsOnly_Details_NameLevels()
    {
        var panel = NewPanel();

        panel.CreateWatch(new ProgramWatch { Name = "w", Mode = ProgramWatchMode.Tag, LevelIds = ["100"] });

        Assert.Equal("Levels (any of): 100", Assert.Single(panel.Watches).Details);
    }

    /// <summary>A course watch names its course codes, marking inactive and deleted courses.</summary>
    [Fact]
    public void CourseWatch_Details_NameCourses()
    {
        var panel = NewPanel();

        panel.CreateWatch(new ProgramWatch
        {
            Name = "w", Mode = ProgramWatchMode.Course, CourseIds = ["c1", "old", "gone"]
        });

        Assert.Equal("Courses: BIOL101, BIOL099 (inactive), (deleted course)", Assert.Single(panel.Watches).Details);
    }

    /// <summary>Editing a watch refreshes its hover text.</summary>
    [Fact]
    public void UpdateWatch_RefreshesDetails()
    {
        var panel = NewPanel();
        var watch = new ProgramWatch { Name = "w", Mode = ProgramWatchMode.Tag, TagIds = ["t1"] };
        panel.CreateWatch(watch);

        panel.UpdateWatch(new ProgramWatch { Id = watch.Id, Name = "w", Mode = ProgramWatchMode.Course, CourseIds = ["c1"] });

        Assert.Equal("Courses: BIOL101", Assert.Single(panel.Watches).Details);
    }
}
