using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Starting and committing a meeting drag on <see cref="ScheduleGridViewModel"/>: refused for readers and while a
/// section card is open (with the grid's message, cleared when the card closes), saved through the repository and
/// selected when the drop changes something, and never saved over a schedule that changed after the lift.
/// View-model level only: the pointer handling in the view is not covered here.
/// </summary>
public sealed class MeetingDragCommitTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json (reloading the semester context saves it).</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;
    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly FocusService _focus;
    private readonly FakeSectionCardState _card = new();
    private readonly string _sectionId;
    private readonly ScheduleGridViewModel _grid;

    /// <summary>Seeds one academic year with one semester, legal start times and a Tue/Thu section, and opens it.</summary>
    public MeetingDragCommitTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);

        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        semesterRepo.Insert(fall);
        new LegalStartTimeRepository(db).Insert(new LegalStartTime { BlockLength = 1.5, StartTimes = [510, 690, 780] }, year.Id);

        var section = new Section
        {
            SemesterId = fall.Id, CourseId = "c-1", SectionCode = "A",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 90 },
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 90 },
            ],
        };
        _sectionRepo.Insert(section);
        _sectionId = section.Id;

        _focus = new FocusService(
            new SubjectRepository(db), new CourseRepository(db), _sectionRepo, semesterRepo, ayRepo,
            _semesterContext, _sectionStore, null);
        _grid = CreateGrid(_fx.Lock);

        _semesterContext.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { fall.Id });
        _sectionStore.Reload(_sectionRepo, [fall.Id]);
    }

    /// <summary>Builds the real grid view model over the temp database with the given lock.</summary>
    private ScheduleGridViewModel CreateGrid(WriteLockService lockService)
    {
        var db = _fx.Db;
        var envRepo = new SchedulingEnvironmentRepository(db);
        var notifier = new GridChangeNotifier();
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        return new ScheduleGridViewModel(
            _sectionRepo, courseRepo, new InstructorRepository(db), new RoomRepository(db), subjectRepo, envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), notifier,
            new InstructorCommitmentRepository(db), lockService, new SharedScheduleService(),
            _focus, FocusTestSupport.NoColours(courseRepo, subjectRepo, _sectionStore), new BlockPatternRepository(db),
            _card, new LegalStartTimeRepository(db), new TermPoint.ViewModels.GridView.SectionMarks(),
            AccessStateTestSupport.For(db, _sectionStore, notifier));
    }

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>The stored meetings as (day, start) pairs.</summary>
    private List<(int Day, int Start)> Stored() =>
        _sectionRepo.GetById(_sectionId)!.Schedule.Select(m => (m.Day, m.StartMinutes)).ToList();

    /// <summary>A reader never lifts a tile, and gets no message.</summary>
    [Fact]
    public void TryBegin_Reader_ReturnsNullWithoutMessage()
    {
        var reader = CreateGrid(new WriteLockService());   // TryAcquire never called: not the writer
        Assert.Null(reader.TryBeginMeetingDrag(_sectionId, 0));
        Assert.Null(reader.LastErrorMessage);
    }

    /// <summary>A writer with no card open gets a session for the pressed meeting.</summary>
    [Fact]
    public void TryBegin_Writer_ReturnsSession()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 1);
        Assert.NotNull(session);
        Assert.Equal(4, session!.OriginalDay);
        Assert.Null(_grid.LastErrorMessage);
    }

    /// <summary>A missing section or meeting index gives no session.</summary>
    [Fact]
    public void TryBegin_UnknownSectionOrIndex_ReturnsNull()
    {
        Assert.Null(_grid.TryBeginMeetingDrag("nope", 0));
        Assert.Null(_grid.TryBeginMeetingDrag(_sectionId, 5));
        Assert.Null(_grid.TryBeginMeetingDrag(_sectionId, -1));
    }

    /// <summary>With a card open the tile does not lift and the message bar says to finish editing first.</summary>
    [Fact]
    public void TryBegin_CardOpen_RefusesWithMessage()
    {
        _card.Heading = "HIST101 A";
        Assert.Null(_grid.TryBeginMeetingDrag(_sectionId, 0));
        Assert.Equal(OpenCardMessages.FinishFirstToDrag("HIST101 A"), _grid.LastErrorMessage);
    }

    /// <summary>The message goes away when the card closes.</summary>
    [Fact]
    public void RefusalMessage_ClearsWhenCardCloses()
    {
        _card.Heading = "HIST101 A";
        _grid.TryBeginMeetingDrag(_sectionId, 0);
        Assert.NotNull(_grid.LastErrorMessage);

        _card.Heading = null;

        Assert.Null(_grid.LastErrorMessage);
    }

    /// <summary>A different error that replaced the message is left alone when the card closes.</summary>
    [Fact]
    public void RefusalMessage_NotClearedIfReplacedByAnotherError()
    {
        _card.Heading = "HIST101 A";
        _grid.TryBeginMeetingDrag(_sectionId, 0);
        _grid.LastErrorMessage = "Some other problem.";

        _card.Heading = null;

        Assert.Equal("Some other problem.", _grid.LastErrorMessage);
    }

    /// <summary>The message stays while a card stays open (another card opening does not clear it).</summary>
    [Fact]
    public void RefusalMessage_StaysWhileCardOpen()
    {
        _card.Heading = "HIST101 A";
        _grid.TryBeginMeetingDrag(_sectionId, 0);

        _card.Heading = "HIST101 B";

        Assert.NotNull(_grid.LastErrorMessage);
    }

    /// <summary>A valid drop saves the moved meeting (the other stays) and selects the section.</summary>
    [Fact]
    public void Commit_SavesMovedMeetingAndSelectsSection()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 1)!;
        var preview = session.Preview(5, 780, free: false, moveAlone: true, 480, 1080);

        Assert.True(_grid.CommitMeetingDrag(session, preview));

        Assert.Equal([(2, 600), (5, 780)], Stored());
        Assert.Contains(_sectionId, _grid.SelectedSectionIds);
    }

    /// <summary>A card opened after the lift refuses the drop: nothing is saved and the message shows.</summary>
    [Fact]
    public void Commit_CardOpenedAtDrop_RefusesAndDoesNotSave()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 1)!;
        var preview = session.Preview(5, 780, free: false, moveAlone: true, 480, 1080);
        _card.Heading = "HIST101 A";

        Assert.False(_grid.CommitMeetingDrag(session, preview));

        Assert.Equal([(2, 600), (4, 600)], Stored());
        Assert.Equal(OpenCardMessages.FinishFirstToDrag("HIST101 A"), _grid.LastErrorMessage);
    }

    /// <summary>A schedule that changed after the lift is never overwritten.</summary>
    [Fact]
    public void Commit_StoredScheduleChangedAfterLift_DoesNotSave()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 1)!;
        var preview = session.Preview(5, 780, free: false, moveAlone: true, 480, 1080);

        var live = _sectionRepo.GetById(_sectionId)!;
        live.Schedule[0].StartMinutes = 510;
        _sectionRepo.Update(live);

        Assert.False(_grid.CommitMeetingDrag(session, preview));

        Assert.Equal([(2, 510), (4, 600)], Stored());
    }

    /// <summary>A drop where the meeting started (no change) saves nothing.</summary>
    [Fact]
    public void Commit_NoChange_DoesNotSave()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 0)!;
        var preview = session.Preview(2, 600, free: true, moveAlone: true, 480, 1080);
        Assert.False(preview.CanDrop);

        Assert.False(_grid.CommitMeetingDrag(session, preview));

        Assert.Equal([(2, 600), (4, 600)], Stored());
    }

    /// <summary>A refused drop (overlapping its own meeting) saves nothing.</summary>
    [Fact]
    public void Commit_Refused_DoesNotSave()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 0)!;
        var preview = session.Preview(4, 600, free: true, moveAlone: true, 480, 1080);
        Assert.True(preview.IsRefused);

        Assert.False(_grid.CommitMeetingDrag(session, preview));

        Assert.Equal([(2, 600), (4, 600)], Stored());
    }

    /// <summary>A reader's drop is not saved even with a good preview.</summary>
    [Fact]
    public void Commit_Reader_DoesNotSave()
    {
        var session = _grid.TryBeginMeetingDrag(_sectionId, 1)!;
        var preview = session.Preview(5, 780, free: false, moveAlone: true, 480, 1080);
        var reader = CreateGrid(new WriteLockService());

        Assert.False(reader.CommitMeetingDrag(session, preview));

        Assert.Equal([(2, 600), (4, 600)], Stored());
    }
}
