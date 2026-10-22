using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// F4: program watches stay usable in a read-only session, for that session only. As a reader (reader / observer
/// mode, or after the write lock was lost) creating, toggling and deleting a watch changes only the panel's
/// in-memory list and never calls the repository (whose database is a throw-away copy); the grid is notified as
/// usual and <see cref="AccessPanelViewModel.GetEnabledWatches"/> — which is what the grid reads — follows. A
/// writer is unchanged: every change is saved.
/// </summary>
public sealed class AccessPanelReadOnlyTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────

    /// <summary>Everything a test needs to drive one panel and see what reached the repository and the grid.</summary>
    private sealed class Rig
    {
        /// <summary>The panel under test.</summary>
        public AccessPanelViewModel Panel { get; set; } = null!;

        /// <summary>The watch repository; strict for a reader so ANY unexpected call fails the test.</summary>
        public Mock<IProgramWatchRepository> Repo { get; init; } = null!;

        /// <summary>The semester selection the panel follows.</summary>
        public SemesterContext Semesters { get; init; } = null!;

        /// <summary>How many times the grid was told its content changed.</summary>
        public int GridNotifications { get; set; }

        /// <summary>Whether the session may write; flip it to model a lock lost mid-session.</summary>
        public bool CanWrite { get; set; }
    }

    /// <summary>
    /// Builds a panel over two semesters (sem-1 selected) in a session that can or cannot write. A reader's
    /// repository mock is STRICT and answers only <c>GetAll</c>, so a save or delete throws and fails the test.
    /// </summary>
    /// <param name="canWrite">True for a writer session.</param>
    private static Rig NewRig(bool canWrite)
    {
        var year = new AcademicYear { Id = "ay-1", Name = "2026-2027" };
        var ayRepo = new Mock<IAcademicYearRepository>();
        ayRepo.Setup(r => r.GetAll()).Returns(new List<AcademicYear> { year });
        var semRepo = new Mock<ISemesterRepository>();
        semRepo.Setup(r => r.GetAll()).Returns(new List<Semester>
        {
            new() { Id = "sem-1", AcademicYearId = "ay-1", Name = "Fall",   SortOrder = 0 },
            new() { Id = "sem-2", AcademicYearId = "ay-1", Name = "Winter", SortOrder = 1 },
        });
        var semesters = new SemesterContext();
        semesters.Reload(ayRepo.Object, semRepo.Object, "ay-1", new HashSet<string> { "sem-1" });

        var repo = new Mock<IProgramWatchRepository>(canWrite ? MockBehavior.Loose : MockBehavior.Strict);
        repo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<ProgramWatch>());

        var notifier = new GridChangeNotifier();
        var rig = new Rig { Repo = repo, Semesters = semesters, CanWrite = canWrite };
        notifier.GridContentChanged += () => rig.GridNotifications++;

        rig.Panel = new AccessPanelViewModel(
            repo.Object, semesters, notifier,
            new Mock<ISchedulingEnvironmentRepository>().Object,
            new Mock<ICourseRepository>().Object,
            () => rig.CanWrite);
        return rig;
    }

    /// <summary>A tag-mode watch with a fresh id.</summary>
    /// <param name="name">The watch's name.</param>
    private static ProgramWatch NewWatch(string name) => new() { Name = name, Mode = ProgramWatchMode.Tag };

    /// <summary>Selects the semester with id <paramref name="semesterId"/> (what the semester picker does).</summary>
    private static void SelectSemester(Rig rig, string semesterId)
        => rig.Semesters.RestoreSelection("ay-1", new HashSet<string> { semesterId });

    // ── A reader: memory only ─────────────────────────────────────────────────

    /// <summary>
    /// As a reader, creating a watch adds it to the panel and to what the grid reads, notifies the grid, and never
    /// touches the repository (a strict mock: any Save or Delete would fail the test).
    /// </summary>
    [Fact]
    public void Reader_CreateWatch_IsInMemoryOnly_AndTheGridIsNotified()
    {
        var rig = NewRig(canWrite: false);
        var before = rig.GridNotifications;

        rig.Panel.CreateWatch(NewWatch("Core + 200"));

        Assert.Equal("Core + 200", Assert.Single(rig.Panel.Watches).Name);
        Assert.Equal("Core + 200", Assert.Single(rig.Panel.GetEnabledWatches()).Name);
        Assert.Equal(before + 1, rig.GridNotifications);
        rig.Repo.Verify(r => r.Save(It.IsAny<string>(), It.IsAny<ProgramWatch>()), Times.Never);
        Assert.Equal("1 watch", rig.Panel.SummaryPrefix);
    }

    /// <summary>
    /// As a reader, toggling a watch off removes it from the enabled set the grid reads; toggling it on restores
    /// it; neither reaches the repository.
    /// </summary>
    [Fact]
    public void Reader_ToggleWatch_ChangesTheEnabledSetOnly()
    {
        var rig = NewRig(canWrite: false);
        rig.Panel.CreateWatch(NewWatch("A"));
        var item = Assert.Single(rig.Panel.Watches);
        var before = rig.GridNotifications;

        item.IsEnabled = false;
        Assert.Empty(rig.Panel.GetEnabledWatches());
        item.IsEnabled = true;
        Assert.Single(rig.Panel.GetEnabledWatches());

        Assert.Equal(before + 2, rig.GridNotifications);
        rig.Repo.Verify(r => r.Save(It.IsAny<string>(), It.IsAny<ProgramWatch>()), Times.Never);
    }

    /// <summary>As a reader, deleting a watch removes it from the panel and the grid's set and never calls the repository.</summary>
    [Fact]
    public void Reader_DeleteWatch_RemovesItInMemoryOnly()
    {
        var rig = NewRig(canWrite: false);
        rig.Panel.CreateWatch(NewWatch("A"));
        rig.Panel.CreateWatch(NewWatch("B"));
        var before = rig.GridNotifications;

        rig.Panel.Watches[0].RequestDeleteCommand.Execute(null);

        Assert.Equal("B", Assert.Single(rig.Panel.Watches).Name);
        Assert.Equal("B", Assert.Single(rig.Panel.GetEnabledWatches()).Name);
        Assert.Equal(before + 1, rig.GridNotifications);
        rig.Repo.Verify(r => r.Delete(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// A reader's changes belong to the session, not to the semester that happened to be showing: switching to the
    /// other semester shows its own (database) watches, and switching back shows the reader's edits again.
    /// </summary>
    [Fact]
    public void Reader_Edits_SurviveASemesterSwitchWithinTheSession()
    {
        var rig = NewRig(canWrite: false);
        rig.Panel.CreateWatch(NewWatch("Mine"));

        SelectSemester(rig, "sem-2");
        Assert.Empty(rig.Panel.Watches);

        SelectSemester(rig, "sem-1");
        Assert.Equal("Mine", Assert.Single(rig.Panel.Watches).Name);
        rig.Repo.Verify(r => r.Save(It.IsAny<string>(), It.IsAny<ProgramWatch>()), Times.Never);
    }

    // ── A writer: unchanged ───────────────────────────────────────────────────

    /// <summary>As a writer, create / toggle / delete are saved to the repository exactly as before.</summary>
    [Fact]
    public void Writer_CreateToggleDelete_AreSavedToTheRepository()
    {
        var rig = NewRig(canWrite: true);
        var watch = NewWatch("A");
        // Create re-reads the saved list (as before), so the repository must now return the watch.
        rig.Repo.Setup(r => r.GetAll("sem-1")).Returns(new List<ProgramWatch> { watch });

        rig.Panel.CreateWatch(watch);
        rig.Repo.Verify(r => r.Save("sem-1", watch), Times.Once);

        var item = Assert.Single(rig.Panel.Watches);
        item.IsEnabled = false;
        rig.Repo.Verify(r => r.Save("sem-1", watch), Times.Exactly(2));

        item.RequestDeleteCommand.Execute(null);
        rig.Repo.Verify(r => r.Delete(watch.Id), Times.Once);
        Assert.Empty(rig.Panel.Watches);
    }

    /// <summary>
    /// The session's ability to write is asked at the moment of each change: a writer that loses the lock keeps
    /// working in memory from then on, with no further repository calls.
    /// </summary>
    [Fact]
    public void LockLostMidSession_LaterChangesStayInMemory()
    {
        var rig = NewRig(canWrite: true);
        rig.Panel.CreateWatch(NewWatch("Saved while writer"));
        rig.Repo.Invocations.Clear();

        rig.CanWrite = false;
        rig.Panel.CreateWatch(NewWatch("Only in memory"));

        Assert.Contains(rig.Panel.Watches, w => w.Name == "Only in memory");
        rig.Repo.Verify(r => r.Save(It.IsAny<string>(), It.IsAny<ProgramWatch>()), Times.Never);
    }
}
