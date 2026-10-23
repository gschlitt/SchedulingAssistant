using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="FocusService"/> (spec item 20, step 20.1) over a real temp database with real repositories
/// (<see cref="ExtensionDataDbFixture"/>): the starting-value rule, persistence in
/// <see cref="AppSettings.FocusByDatabase"/>, the demo's never-persisted focus, the editor-open guard, the change
/// event, Matches / IsCourseInFocus / IsInFocus, and cache invalidation.
/// <para>
/// The settings file is protected by <see cref="SettingsFileGuard"/>, and every test starts with an empty
/// <c>FocusByDatabase</c> and uses its own database key, so no test sees another's remembered focus.
/// </para>
/// </summary>
public sealed class FocusServiceTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Protects settings.json: the service saves it, and SemesterContext does too.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database with the full schema.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SubjectRepository _subjectRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    /// <summary>The settings key this test's databases use (unique per test instance).</summary>
    private readonly string _key = "c:\\test\\" + Guid.NewGuid().ToString("N") + ".db";

    /// <summary>Services created by <see cref="Make"/>, disposed with the fixture.</summary>
    private readonly List<FocusService> _services = new();

    private const string YearOld = "2024-2025";
    private const string YearNew = "2025-2026";
    private string _ayOld = "";
    private string _ayNew = "";
    private string _semOld = "";
    private string _semNew = "";

    /// <summary>Opens the database, seeds two academic years with one semester each, and clears remembered foci.</summary>
    public FocusServiceTests()
    {
        var db = _fx.Db;
        _subjectRepo = new SubjectRepository(db);
        _courseRepo = new CourseRepository(db);
        _sectionRepo = new SectionRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);

        AppSettings.Current.FocusByDatabase.Clear();

        _ayOld = AddYear(YearOld, out _semOld);
        _ayNew = AddYear(YearNew, out _semNew);
    }

    /// <summary>Disposes the services and database, and restores the settings file.</summary>
    public void Dispose()
    {
        foreach (var s in _services) s.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Inserts an academic year with a Fall semester; returns the year's id and the semester's id.</summary>
    private string AddYear(string name, out string semesterId)
    {
        var ay = new AcademicYear { Name = name };
        _ayRepo.Insert(ay);
        var sem = new Semester { AcademicYearId = ay.Id, Name = "Fall" };
        _semesterRepo.Insert(sem);
        semesterId = sem.Id;
        return ay.Id;
    }

    /// <summary>Inserts <paramref name="count"/> subjects named S1..Sn and returns them.</summary>
    private List<Subject> AddSubjects(int count)
    {
        var list = new List<Subject>();
        for (var i = 1; i <= count; i++)
        {
            var s = new Subject { Name = "Subject " + i, CalendarAbbreviation = "S" + i };
            _subjectRepo.Insert(s);
            list.Add(s);
        }
        return list;
    }

    /// <summary>Inserts a course in the subject and returns it.</summary>
    private Course AddCourse(Subject subject, string code)
    {
        var c = new Course { SubjectId = subject.Id, CalendarCode = code, Title = code };
        _courseRepo.Insert(c);
        return c;
    }

    /// <summary>Inserts a section of the course in the semester, taught by the given instructors, and returns it.</summary>
    private Section AddSection(Course course, string semesterId, string code, params string[] instructorIds)
    {
        var s = new Section
        {
            CourseId = course.Id,
            SemesterId = semesterId,
            SectionCode = code,
            Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 90 }],
            InstructorAssignments = instructorIds.Select(i => new InstructorAssignment { InstructorId = i }).ToList(),
        };
        _sectionRepo.Insert(s);
        return s;
    }

    /// <summary>Creates a service over the fixture's repositories; <paramref name="key"/> null means the demo.</summary>
    private FocusService Make(string? key)
    {
        var svc = new FocusService(_subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo,
            _semesterContext, _sectionStore, key);
        _services.Add(svc);
        return svc;
    }

    /// <summary>Creates a service for this test's database key.</summary>
    private FocusService Make() => Make(_key);

    /// <summary>The focus remembered in the in-memory settings for this test's key, or null when none.</summary>
    private SubjectFocus? Remembered(string? key = null) =>
        AppSettings.Current.FocusByDatabase.TryGetValue(key ?? _key, out var saved)
            ? SubjectFocus.FromSaved(saved)
            : null;

    /// <summary>Re-reads settings.json from disk (as a restart would) and returns the focus saved for the key, or null.</summary>
    private SubjectFocus? RememberedOnDisk(string? key = null) =>
        AppSettings.Load().FocusByDatabase.TryGetValue(key ?? _key, out var saved)
            ? SubjectFocus.FromSaved(saved)
            : null;

    /// <summary>Makes <paramref name="yearId"/> the viewed academic year.</summary>
    private void View(string yearId) =>
        _semesterContext.Reload(_ayRepo, _semesterRepo, restoreAcademicYearId: yearId);

    // ── Starting value ────────────────────────────────────────────────────────

    /// <summary>A database with three subjects (the limit) starts at All, and the decision is saved to disk.</summary>
    [Fact]
    public void FirstSeen_ThreeSubjects_StartsAtAll_AndSaves()
    {
        AddSubjects(FocusService.MaxSubjectsForAllStart);

        var svc = Make();

        Assert.Equal(3, FocusService.MaxSubjectsForAllStart);
        Assert.Equal(SubjectFocus.All, svc.Current);
        Assert.Equal(SubjectFocus.All, RememberedOnDisk());
    }

    /// <summary>A database with four subjects starts at None (the empty state), saved to disk.</summary>
    [Fact]
    public void FirstSeen_FourSubjects_StartsAtNone_AndSaves()
    {
        AddSubjects(FocusService.MaxSubjectsForAllStart + 1);

        var svc = Make();

        Assert.Equal(SubjectFocus.None, svc.Current);
        Assert.Equal(SubjectFocus.None, RememberedOnDisk());
    }

    /// <summary>A brand-new database with no subjects at all starts at All.</summary>
    [Fact]
    public void FirstSeen_NoSubjects_StartsAtAll()
    {
        Assert.Equal(SubjectFocus.All, Make().Current);
    }

    /// <summary>The first-seen rule runs once: a database that later grows past three subjects stays at All.</summary>
    [Fact]
    public void FirstSeenRule_RunsOnce_GrowthDoesNotChangeIt()
    {
        AddSubjects(2);
        Assert.Equal(SubjectFocus.All, Make().Current);

        AddSubjects(5);
        Assert.Equal(SubjectFocus.All, Make().Current);
    }

    /// <summary>A remembered None stays None even for a small database (it is not "first seen" any more).</summary>
    [Fact]
    public void Remembered_None_IsKept_ForSmallDatabase()
    {
        AddSubjects(1);
        AppSettings.Current.FocusByDatabase[_key] = SubjectFocus.None.ToSaved();

        Assert.Equal(SubjectFocus.None, Make().Current);
    }

    /// <summary>A second instance with the same key gets the first one's focus, also after the settings are re-read from disk.</summary>
    [Fact]
    public void RememberedFocus_IsReloadedBySecondInstance_AndAfterRestart()
    {
        var subjects = AddSubjects(5);
        var first = Make();
        var chosen = SubjectFocus.ForSubjects([subjects[1].Id, subjects[3].Id]);
        Assert.True(first.SetFocus(chosen));

        Assert.Equal(chosen, Make().Current);

        // A restart: the in-memory settings are replaced by what is on disk.
        AppSettings.Load();
        Assert.Equal(chosen, Make().Current);
    }

    /// <summary>Different keys (different databases) keep independent foci.</summary>
    [Fact]
    public void DifferentKeys_AreIndependent()
    {
        AddSubjects(5);
        var a = Make(_key);
        var b = Make(_key + "-other");
        Assert.True(a.SetFocus(SubjectFocus.All));

        Assert.Equal(SubjectFocus.All, a.Current);
        Assert.Equal(SubjectFocus.None, b.Current);
    }

    /// <summary>A subject deleted since the focus was saved drops out silently, and the pruned focus is saved.</summary>
    [Fact]
    public void DeletedSubject_IsDroppedFromRememberedFocus_AndSaved()
    {
        var subjects = AddSubjects(5);
        AppSettings.Current.FocusByDatabase[_key] =
            SubjectFocus.ForSubjects([subjects[0].Id, "deleted-subject"]).ToSaved();

        var svc = Make();

        var expected = SubjectFocus.ForSubjects([subjects[0].Id]);
        Assert.Equal(expected, svc.Current);
        Assert.Equal(expected, RememberedOnDisk());
    }

    /// <summary>If every focused subject was deleted, the focus becomes None (the empty state), and that is saved.</summary>
    [Fact]
    public void AllFocusedSubjectsDeleted_BecomesNone_AndSaved()
    {
        AddSubjects(5);
        AppSettings.Current.FocusByDatabase[_key] = SubjectFocus.ForSubjects(["gone-1", "gone-2"]).ToSaved();

        var svc = Make();

        Assert.Equal(SubjectFocus.None, svc.Current);
        Assert.Equal(SubjectFocus.None, RememberedOnDisk());
    }

    /// <summary>A remembered focus with every subject still present is left exactly as it was (no needless save).</summary>
    [Fact]
    public void RememberedFocus_AllSubjectsPresent_Unchanged()
    {
        var subjects = AddSubjects(5);
        var focus = SubjectFocus.ForSubjects([subjects[2].Id]);
        AppSettings.Current.FocusByDatabase[_key] = focus.ToSaved();

        Assert.Equal(focus, Make().Current);
    }

    // ── Demo ───────────────────────────────────────────────────────────────────

    /// <summary>The demo (null key) always starts at All, even with many subjects, and writes nothing to settings.</summary>
    [Fact]
    public void Demo_StartsAtAll_AndWritesNothing()
    {
        AddSubjects(10);

        var svc = Make(null);

        Assert.Equal(SubjectFocus.All, svc.Current);
        Assert.Empty(AppSettings.Current.FocusByDatabase);
    }

    /// <summary>In the demo a change lasts for the session only: it is applied and announced but never written.</summary>
    [Fact]
    public void Demo_SetFocus_ChangesInMemoryOnly()
    {
        var subjects = AddSubjects(10);
        var svc = Make(null);
        var raised = 0;
        svc.FocusChanged += () => raised++;

        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));

        Assert.Equal(1, raised);
        Assert.Equal(SubjectFocus.ForSubjects([subjects[0].Id]), svc.Current);
        Assert.Empty(AppSettings.Current.FocusByDatabase);
        Assert.Empty(AppSettings.Load().FocusByDatabase);
    }

    // ── SetFocus, the editor guard and the event ───────────────────────────────

    /// <summary>While an editor is open, SetFocus returns false and changes nothing: no event, no save, same focus.</summary>
    [Fact]
    public void SetFocus_RefusedWhileEditorOpen()
    {
        AddSubjects(5);                       // starts at None
        var svc = Make();
        var raised = 0;
        svc.FocusChanged += () => raised++;

        svc.SetEditorOpen(true);
        Assert.False(svc.CanChangeFocus);
        Assert.False(svc.SetFocus(SubjectFocus.All));

        Assert.Equal(0, raised);
        Assert.Equal(SubjectFocus.None, svc.Current);
        Assert.Equal(SubjectFocus.None, RememberedOnDisk());

        svc.SetEditorOpen(false);
        Assert.True(svc.CanChangeFocus);
        Assert.True(svc.SetFocus(SubjectFocus.All));
        Assert.Equal(1, raised);
        Assert.Equal(SubjectFocus.All, RememberedOnDisk());
    }

    /// <summary>CanChangeFocus is observable: PropertyChanged fires when the editor opens and closes, and only then.</summary>
    [Fact]
    public void CanChangeFocus_RaisesPropertyChanged()
    {
        var svc = Make();
        var names = new List<string?>();
        ((INotifyPropertyChanged)svc).PropertyChanged += (_, e) => names.Add(e.PropertyName);

        svc.SetEditorOpen(true);
        svc.SetEditorOpen(true);     // no change, no notification
        svc.SetEditorOpen(false);

        Assert.Equal([nameof(FocusService.CanChangeFocus), nameof(FocusService.CanChangeFocus)], names);
    }

    /// <summary>FocusChanged is raised once for a real change, not for setting the same value, and the new value is saved.</summary>
    [Fact]
    public void FocusChanged_RaisedOncePerRealChange()
    {
        var subjects = AddSubjects(5);        // None
        var svc = Make();
        var raised = 0;
        svc.FocusChanged += () => raised++;
        var focus = SubjectFocus.ForSubjects([subjects[0].Id]);

        Assert.True(svc.SetFocus(focus));
        Assert.Equal(1, raised);
        Assert.Equal(focus, RememberedOnDisk());

        // The same value again (a different instance with equal content): succeeds, but nothing happens.
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        Assert.Equal(1, raised);

        Assert.True(svc.SetFocus(SubjectFocus.All));
        Assert.Equal(2, raised);
    }

    // ── Matches / IsCourseInFocus ──────────────────────────────────────────────

    /// <summary>Matches and IsCourseInFocus under All, None and a Subjects focus.</summary>
    [Fact]
    public void MatchesAndIsCourseInFocus_FollowTheFocus()
    {
        var subjects = AddSubjects(5);
        var inCourse = AddCourse(subjects[0], "S1-101");
        var outCourse = AddCourse(subjects[1], "S2-101");
        var inSection = AddSection(inCourse, _semNew, "A");
        var outSection = AddSection(outCourse, _semNew, "A");
        var noCourse = new Section { Id = "x", CourseId = null, SemesterId = _semNew };
        var unknownCourse = new Section { Id = "y", CourseId = "no-such-course", SemesterId = _semNew };
        var svc = Make();

        Assert.True(svc.SetFocus(SubjectFocus.All));
        Assert.All(new[] { inSection, outSection, noCourse, unknownCourse }, s => Assert.True(svc.Matches(s)));
        Assert.True(svc.IsCourseInFocus(inCourse));
        Assert.True(svc.IsCourseInFocus(outCourse));

        Assert.True(svc.SetFocus(SubjectFocus.None));
        Assert.All(new[] { inSection, outSection, noCourse, unknownCourse }, s => Assert.False(svc.Matches(s)));
        Assert.False(svc.IsCourseInFocus(inCourse));

        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        Assert.True(svc.Matches(inSection));
        Assert.False(svc.Matches(outSection));
        Assert.False(svc.Matches(noCourse));
        Assert.False(svc.Matches(unknownCourse));
        Assert.True(svc.IsCourseInFocus(inCourse));
        Assert.False(svc.IsCourseInFocus(outCourse));

        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id, subjects[1].Id])));
        Assert.True(svc.Matches(outSection));
    }

    // ── IsInFocus end to end ───────────────────────────────────────────────────

    /// <summary>
    /// Real repositories, real viewed year: an instructor who teaches the focus subject this year is in focus, one
    /// who teaches another subject is not, a new hire is, an inactive one is not, and All/None short-circuit.
    /// </summary>
    [Fact]
    public void IsInFocus_EndToEnd()
    {
        var subjects = AddSubjects(5);
        var biol = AddCourse(subjects[0], "S1-101");
        var engl = AddCourse(subjects[1], "S2-101");
        AddSection(biol, _semNew, "A", "teachesBiol");
        AddSection(engl, _semNew, "A", "teachesEngl");
        AddSection(engl, _semOld, "B", "oldEnglOnly");     // last year ENGL only (inside the window)
        View(_ayNew);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));

        Assert.True(svc.IsInFocus(FocusWorld.Instructor("teachesBiol")));
        Assert.False(svc.IsInFocus(FocusWorld.Instructor("teachesEngl")));
        Assert.False(svc.IsInFocus(FocusWorld.Instructor("oldEnglOnly")));
        Assert.True(svc.IsInFocus(FocusWorld.Instructor("newHire")));
        Assert.False(svc.IsInFocus(FocusWorld.Instructor("teachesBiol", isActive: false)));

        Assert.True(svc.SetFocus(SubjectFocus.All));
        Assert.True(svc.IsInFocus(FocusWorld.Instructor("teachesEngl")));
        Assert.True(svc.IsInFocus(FocusWorld.Instructor("teachesBiol", isActive: false)));

        Assert.True(svc.SetFocus(SubjectFocus.None));
        Assert.False(svc.IsInFocus(FocusWorld.Instructor("newHire")));
    }

    /// <summary>The window follows the VIEWED year: viewing last year, an instructor who only teaches this year is "after Y" (step 6).</summary>
    [Fact]
    public void IsInFocus_UsesTheViewedAcademicYear()
    {
        var subjects = AddSubjects(5);
        var engl = AddCourse(subjects[1], "S2-101");
        AddSection(engl, _semNew, "A", "onlyThisYear");
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));

        View(_ayNew);
        Assert.False(svc.IsInFocus(FocusWorld.Instructor("onlyThisYear")));    // ENGL in the window

        View(_ayOld);
        Assert.True(svc.IsInFocus(FocusWorld.Instructor("onlyThisYear")));     // ENGL is after Y: step 6
    }

    // ── Invalidate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The teaching history is cached; Invalidate makes the next IsInFocus see a newly added section.
    /// </summary>
    [Fact]
    public void Invalidate_PicksUpNewlyAddedSection()
    {
        var subjects = AddSubjects(5);
        var engl = AddCourse(subjects[1], "S2-101");
        View(_ayNew);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        var instructor = FocusWorld.Instructor("x");
        Assert.True(svc.IsInFocus(instructor));                  // new hire: step 6, and the history is now cached

        AddSection(engl, _semNew, "A", "x");
        Assert.True(svc.IsInFocus(instructor));                  // still the cached answer

        svc.Invalidate();
        Assert.False(svc.IsInFocus(instructor));                 // ENGL in the window now
    }

    /// <summary>A section-store reload (what every section save does) invalidates the caches, including the course map.</summary>
    [Fact]
    public void SectionStoreReload_Invalidates_History_AndCourseMap()
    {
        var subjects = AddSubjects(5);
        var engl = AddCourse(subjects[1], "S2-101");
        View(_ayNew);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        var instructor = FocusWorld.Instructor("x");
        Assert.True(svc.IsInFocus(instructor));
        Assert.False(svc.Matches(new Section { CourseId = engl.Id }));    // builds and caches the course map

        // A course added later, then a section of it, then the usual reload.
        var lateCourse = AddCourse(subjects[0], "S1-201");
        var lateSection = AddSection(lateCourse, _semNew, "A", "x");
        _sectionStore.Reload(_sectionRepo, [_semNew]);

        Assert.True(svc.Matches(lateSection));                   // the course map was rebuilt
        Assert.True(svc.IsInFocus(instructor));                  // x now teaches S1 in the window: still in focus, via step 4

        AddSection(engl, _semNew, "B", "x");
        _sectionStore.Reload(_sectionRepo, [_semNew]);
        Assert.True(svc.IsInFocus(instructor));                  // S1 and S2 both in the window: Biology still matches
    }

    /// <summary>Changing the viewed academic year invalidates the cached history.</summary>
    [Fact]
    public void ViewedYearChange_Invalidates()
    {
        var subjects = AddSubjects(5);
        var engl = AddCourse(subjects[1], "S2-101");
        View(_ayNew);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        var instructor = FocusWorld.Instructor("x");
        Assert.True(svc.IsInFocus(instructor));                  // cached: x has no sections

        AddSection(engl, _semNew, "A", "x");
        _semesterContext.SelectedAcademicYear = _semesterContext.AcademicYears.First(y => y.Id == _ayOld);
        _semesterContext.SelectedAcademicYear = _semesterContext.AcademicYears.First(y => y.Id == _ayNew);

        Assert.False(svc.IsInFocus(instructor));                 // rebuilt: ENGL in the window
    }

    /// <summary>After Dispose the service no longer reacts to section-store reloads.</summary>
    [Fact]
    public void Dispose_Unsubscribes()
    {
        var subjects = AddSubjects(5);
        var engl = AddCourse(subjects[1], "S2-101");
        View(_ayNew);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        var instructor = FocusWorld.Instructor("x");
        Assert.True(svc.IsInFocus(instructor));

        svc.Dispose();
        AddSection(engl, _semNew, "A", "x");
        _sectionStore.Reload(_sectionRepo, [_semNew]);

        Assert.True(svc.IsInFocus(instructor));                  // cache was not dropped
    }

    // ── PruneDeletedSubjects (step 20.3) ───────────────────────────────────────

    /// <summary>
    /// Deleting a focused subject mid-session drops it at once: the focus changes, is saved, and FocusChanged is
    /// raised once. The other focused subject stays.
    /// </summary>
    [Fact]
    public void Prune_DropsDeletedSubject_Immediately_SavesAndRaises()
    {
        var subjects = AddSubjects(5);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id, subjects[1].Id])));
        var raised = 0;
        svc.FocusChanged += () => raised++;

        _subjectRepo.Delete(subjects[1].Id);
        svc.PruneDeletedSubjects();

        var expected = SubjectFocus.ForSubjects([subjects[0].Id]);
        Assert.Equal(expected, svc.Current);
        Assert.Equal(expected, RememberedOnDisk());
        Assert.Equal(1, raised);
    }

    /// <summary>If the only focused subject is deleted, the focus becomes None (the empty state).</summary>
    [Fact]
    public void Prune_LastFocusedSubjectDeleted_BecomesNone()
    {
        var subjects = AddSubjects(5);
        var svc = Make();
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[2].Id])));

        _subjectRepo.Delete(subjects[2].Id);
        svc.PruneDeletedSubjects();

        Assert.Equal(SubjectFocus.None, svc.Current);
        Assert.Equal(SubjectFocus.None, RememberedOnDisk());
    }

    /// <summary>Nothing deleted, or an unfocused subject deleted, or All/None: no change, no save, no event.</summary>
    [Fact]
    public void Prune_NothingToDrop_RaisesNoEvent()
    {
        var subjects = AddSubjects(5);
        var svc = Make();
        var raised = 0;
        svc.FocusChanged += () => raised++;

        svc.PruneDeletedSubjects();                       // None
        Assert.True(svc.SetFocus(SubjectFocus.All));
        raised = 0;
        svc.PruneDeletedSubjects();                       // All
        Assert.True(svc.SetFocus(SubjectFocus.ForSubjects([subjects[0].Id])));
        raised = 0;
        svc.PruneDeletedSubjects();                       // Subjects, all present
        _subjectRepo.Delete(subjects[4].Id);              // an unfocused subject
        svc.PruneDeletedSubjects();

        Assert.Equal(0, raised);
        Assert.Equal(SubjectFocus.ForSubjects([subjects[0].Id]), svc.Current);
    }

    /// <summary>
    /// A prune asked for while an editor is open is deferred: nothing changes (rule 3), and it runs, raising
    /// FocusChanged once, when the editor closes.
    /// </summary>
    [Fact]
    public void Prune_WhileEditorOpen_IsDeferredUntilEditorCloses()
    {
        var subjects = AddSubjects(5);
        var svc = Make();
        var focus = SubjectFocus.ForSubjects([subjects[0].Id, subjects[1].Id]);
        Assert.True(svc.SetFocus(focus));
        var raised = 0;
        svc.FocusChanged += () => raised++;

        svc.SetEditorOpen(true);
        _subjectRepo.Delete(subjects[1].Id);
        svc.PruneDeletedSubjects();

        Assert.Equal(focus, svc.Current);                 // untouched while editing
        Assert.Equal(0, raised);

        svc.SetEditorOpen(false);

        Assert.Equal(SubjectFocus.ForSubjects([subjects[0].Id]), svc.Current);
        Assert.Equal(1, raised);

        svc.SetEditorOpen(true);                          // the pending flag was cleared: no second prune
        svc.SetEditorOpen(false);
        Assert.Equal(1, raised);
    }
}
