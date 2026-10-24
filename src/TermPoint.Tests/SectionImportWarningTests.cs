using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;
using K = TermPoint.Services.SectionImport.ImportWarningKinds;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the grouped warnings of <see cref="SectionImportPlanner"/> (step 24.4): legal start
/// times, an existing course's title, inactive records, a new instructor's last name, near-miss new
/// setup values, a room's campus, overlapping meetings, and the order they come in.
/// </summary>
public class SectionImportWarningTests
{
    /// <summary>The warnings of one kind.</summary>
    private static List<ImportWarning> Of(ImportReport report, string kind) =>
        report.Warnings.Where(w => w.Kind == kind).ToList();

    /// <summary>Start times for the legal-start-time tests: 1.5-hour blocks at 0830, 1000 and 1130; 3-hour blocks at 0830.</summary>
    private static LegalStartTime[] StartTimes() => new[] { Legal(1.5, 830, 1000, 1130), Legal(3.0, 830) };

    // ── Legal start times ─────────────────────────────────────────────────────

    /// <summary>A Begin that is in the list for its length gives no warning.</summary>
    [Fact]
    public void StartTime_LegalForItsLength_NoWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "1000", "1130")), Snapshot(legalStartTimes: StartTimes()));

        Assert.Empty(report.Warnings);
    }

    /// <summary>A Begin that is legal for another length but not for this one is a warning, naming the length.</summary>
    [Fact]
    public void StartTime_NotLegalForItsLength_IsAWarning()
    {
        // 1130 is a legal start for 1.5-hour blocks but the list for 3-hour blocks has only 0830.
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "1130", "1430")), Snapshot(legalStartTimes: StartTimes()));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.LegalStartTime, warning.Kind);
        Assert.Equal(new[] { 2 }, warning.Rows);
        Assert.Equal("1130 isn't a legal start time for 3-hour meetings (1 meeting: row 2). " +
                     "To make it one, add it in Configuration, under Scheduling, and check again.", warning.Message);
    }

    /// <summary>Half-hour lengths are written in hours.</summary>
    [Fact]
    public void StartTime_LengthText_HoursOrHalfHours()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "1045", "1215")), Snapshot(legalStartTimes: StartTimes()));

        Assert.StartsWith("1045 isn't a legal start time for 1.5-hour meetings (1 meeting: row 2).",
            Assert.Single(report.Warnings).Message);
    }

    /// <summary>A length with no list of its own is checked against every start time, and the message omits the length.</summary>
    [Fact]
    public void StartTime_CustomLength_IsCheckedAgainstTheUnion()
    {
        var snapshot = Snapshot(legalStartTimes: StartTimes());

        // 50 minutes has no list: 0830 is in the union, 1045 is not.
        var legal = SectionImportPlanner.Plan(Sheet(Mtg(2, "CHEM", "101", "A", "M", "0830", "0920")), snapshot);
        Assert.Empty(legal.Warnings);

        var illegal = SectionImportPlanner.Plan(Sheet(Mtg(2, "CHEM", "101", "A", "M", "1045", "1135")), snapshot);
        var warning = Assert.Single(illegal.Warnings);
        Assert.Equal("1045 isn't a legal start time (1 meeting: row 2). To make it one, add it in Configuration, under Scheduling, and check again.",
            warning.Message);
    }

    /// <summary>A length whose entry has an empty list counts as having none, as in the editor.</summary>
    [Fact]
    public void StartTime_AnEntryWithAnEmptyList_CountsAsNone()
    {
        var snapshot = Snapshot(legalStartTimes: new[] { Legal(1.0), Legal(1.5, 1000) });

        var report = SectionImportPlanner.Plan(Sheet(Mtg(2, "CHEM", "101", "A", "M", "1000", "1100")), snapshot);

        Assert.Empty(report.Warnings);
    }

    /// <summary>The count is of meetings, the rows are distinct: a row with two days counts twice.</summary>
    [Fact]
    public void StartTime_CountsMeetings_NotRows()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M,W", "1045", "1215")), Snapshot(legalStartTimes: StartTimes()));

        Assert.Contains("(2 meetings: row 2)", Assert.Single(report.Warnings).Message);
    }

    /// <summary>Thirty meetings at one illegal time give exactly one warning, naming five rows and counting the rest.</summary>
    [Fact]
    public void StartTime_ThirtyMeetings_GiveOneWarning()
    {
        var rows = Enumerable.Range(0, 30)
            .Select(i => Mtg(i + 2, "CHEM", "101", $"S{i}", "M", "1045", "1215"))
            .ToArray();

        var report = SectionImportPlanner.Plan(Sheet(rows), Snapshot(legalStartTimes: StartTimes()));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.LegalStartTime, warning.Kind);
        Assert.Equal(Enumerable.Range(2, 30).ToArray(), warning.Rows.ToArray());
        Assert.Equal("1045 isn't a legal start time for 1.5-hour meetings (30 meetings: rows 2, 3, 4, 5, 6 and 25 more). " +
                     "To make it one, add it in Configuration, under Scheduling, and check again.", warning.Message);
    }

    /// <summary>Different illegal times or lengths are separate warnings.</summary>
    [Fact]
    public void StartTime_DifferentTimes_AreSeparateWarnings()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1045", "1215"),
                Mtg(3, "CHEM", "101", "B", "M", "1145", "1315"),
                Mtg(4, "CHEM", "101", "C", "M", "1045", "1215")),
            Snapshot(legalStartTimes: StartTimes()));

        Assert.Equal(2, report.Warnings.Count);
        Assert.Equal(new[] { 2, 4 }, report.Warnings[0].Rows);
        Assert.Equal(new[] { 3 }, report.Warnings[1].Rows);
    }

    /// <summary>With no start times set up at all, there are no per-meeting warnings and one file-wide one.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartTime_NoneSetUp_GivesOneFileWideWarning(bool withEmptyEntry)
    {
        var snapshot = withEmptyEntry ? Snapshot(legalStartTimes: new[] { Legal(1.5) }) : Snapshot();
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1045", "1215"),
                Mtg(3, "CHEM", "101", "B", "T", "1045", "1215")),
            snapshot);

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.NoLegalStartTimes, warning.Kind);
        Assert.Empty(warning.Rows);
        Assert.Equal("2026-2027 has no legal start times set up, so start times weren't checked. " +
                     "You can add them in Configuration, under Scheduling.", warning.Message);
    }

    /// <summary>A file with no meetings has nothing to check, so even a year with no start times says nothing.</summary>
    [Fact]
    public void StartTime_NoMeetings_NoWarning()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.Empty(report.Warnings);
    }

    // ── An existing course's title ────────────────────────────────────────────

    /// <summary>A different title for an existing course is one warning naming every row that gives it.</summary>
    [Fact]
    public void ExistingCourseTitle_Differs_IsGroupedByCourse()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "General Chemistry");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.CourseTitle, "Gen Chem")),
                Sec(3, "CHEM", "101", "B", (C.CourseTitle, "General Chemistry")),
                Sec(4, "CHEM", "101", "C", (C.CourseTitle, "Chem 1"))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.CourseTitleDiffers, warning.Kind);
        Assert.Equal(new[] { 2, 4 }, warning.Rows);
        Assert.Equal("CHEM101 is already called General Chemistry in TermPoint, " +
                     "so the file's title, Gen Chem, isn't used (rows 2 and 4).", warning.Message);
    }

    /// <summary>The same title (ignoring case and spacing), or none, is no warning.</summary>
    [Fact]
    public void ExistingCourseTitle_SameOrBlank_NoWarning()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "General Chemistry");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.CourseTitle, " general  CHEMISTRY")),
                Sec(3, "CHEM", "101", "B", (C.CourseTitle, ""))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }));

        Assert.Empty(report.Warnings);
    }

    // ── Inactive ──────────────────────────────────────────────────────────────

    /// <summary>An inactive existing course is one warning naming the sections' rows.</summary>
    [Fact]
    public void InactiveCourse_IsGroupedByCourse()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "Intro");
        course.IsActive = false;
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "101", "B")),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.Inactive, warning.Kind);
        Assert.Equal(new[] { 2, 3 }, warning.Rows);
        Assert.Equal("CHEM101 is marked inactive in TermPoint (rows 2 and 3). " +
                     "Its sections will still be added; reactivate it in Courses if it's offered again.", warning.Message);
    }

    /// <summary>An inactive existing instructor is one warning naming the rows that give them.</summary>
    [Fact]
    public void InactiveInstructor_IsGroupedByInstructor()
    {
        var smith = Inst("Smith", "John", "JS");
        smith.IsActive = false;
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John")),
                Sec(3, "CHEM", "101", "B", (C.Instructors, "Smith, John"))),
            Snapshot(instructors: new[] { smith }));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.Inactive, warning.Kind);
        Assert.Equal("John Smith is marked inactive in TermPoint (rows 2 and 3). " +
                     "Their sections will still be added; reactivate them in Instructors if they're teaching again.",
            warning.Message);
    }

    /// <summary>A new course or instructor is never "inactive".</summary>
    [Fact]
    public void NewCourseAndInstructor_AreNotInactive()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John"))), Snapshot());

        Assert.DoesNotContain(report.Warnings, w => w.Kind == K.Inactive);
    }

    // ── A new instructor's last name ──────────────────────────────────────────

    /// <summary>A new instructor sharing a last name with an existing one is one warning naming them.</summary>
    [Fact]
    public void NewInstructor_SharingALastName_IsAWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, Jon")),
                Sec(3, "CHEM", "101", "B", (C.Instructors, "Smith, Jon"))),
            Snapshot(instructors: new[] { Inst("Smith", "John", "JS") }));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.InstructorLastName, warning.Kind);
        Assert.Equal(new[] { 2, 3 }, warning.Rows);
        Assert.Equal("Will add Jon Smith. John Smith already exists (rows 2 and 3).", warning.Message);
    }

    /// <summary>When several existing instructors share the last name, up to two are named and the rest counted.</summary>
    [Fact]
    public void NewInstructor_SeveralExisting_NamesTwo()
    {
        var existing = new[]
        {
            Inst("Smith", "John", "JS"), Inst("Smith", "Jane", "JS2"), Inst("Smith", "Jim", "JS3"),
        };
        var two = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, Jon"))),
            Snapshot(instructors: existing.Take(2)));
        var three = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, Jon"))),
            Snapshot(instructors: existing));

        Assert.Equal("Will add Jon Smith. John Smith and Jane Smith already exist (row 2).", Assert.Single(two.Warnings).Message);
        Assert.Equal("Will add Jon Smith. John Smith, Jane Smith and 1 more already exist (row 2).",
            Assert.Single(three.Warnings).Message);
    }

    /// <summary>An instructor who matches an existing one is not new, so there is no warning.</summary>
    [Fact]
    public void ExistingInstructor_NoLastNameWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John"))),
            Snapshot(instructors: new[] { Inst("Smith", "John", "JS") }));

        Assert.Empty(report.Warnings);
    }

    // ── Near-miss setup values ────────────────────────────────────────────────

    /// <summary>A new tag close to an existing tag is a warning.</summary>
    [Fact]
    public void NearMiss_NewTagVersusExisting()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Writng")), Sec(3, "CHEM", "101", "B", (C.Tags, "Writng"))),
            Snapshot(tags: new[] { Val("Writing") }));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(K.NearMiss, warning.Kind);
        Assert.Equal(new[] { 2, 3 }, warning.Rows);
        Assert.Equal("Will add tag Writng. Writing already exists (rows 2 and 3).", warning.Message);
    }

    /// <summary>Two new tags close to each other are one warning, reported once.</summary>
    [Fact]
    public void NearMiss_TwoNewTags_AreReportedOnce()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Lab")), Sec(3, "CHEM", "101", "B", (C.Tags, "Labs"))),
            Snapshot());

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(new[] { 2, 3 }, warning.Rows);
        Assert.Equal("Will add tags Lab and Labs. Check they're meant to be different (rows 2 and 3).", warning.Message);
    }

    /// <summary>Short names that differ by a letter (LAB / LAW) are not a near miss.</summary>
    [Fact]
    public void NearMiss_LabAndLaw_NoWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "LAW"))), Snapshot(tags: new[] { Val("LAB") }));

        Assert.Empty(report.Warnings);
    }

    /// <summary>Values of different kinds are never compared with each other.</summary>
    [Fact]
    public void NearMiss_DifferentKinds_AreNotCompared()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Lecture"))), Snapshot(sectionTypes: new[] { Val("Lecture ") }, meetingTypes: new[] { Val("Lectures") }));

        Assert.Empty(report.Warnings);
    }

    /// <summary>A new value that is not close to anything is no warning, however many are new.</summary>
    [Fact]
    public void NearMiss_NewButDistinct_NoWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Honours"), (C.SectionType, "Seminar"), (C.Campus, "Chilliwack"))),
            Snapshot(tags: new[] { Val("Writing") }, sectionTypes: new[] { Val("Lecture") }, campuses: new[] { Camp("Abbotsford") }));

        Assert.Empty(report.Warnings);
    }

    /// <summary>The other kinds of value (subject, section type, meeting type, campus) are compared by their own names.</summary>
    [Fact]
    public void NearMiss_EachKindOfValue()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEMS", "101", "A", "M", "1000", "1100",
                (C.SectionType, "Lec"), (C.Campus, "Chilliwak"), (C.MeetingType, "Labb"))),
            Snapshot(subjects: new[] { Subj("CHEM") }, sectionTypes: new[] { Val("Lecture") },
                campuses: new[] { Camp("Chilliwack") }, meetingTypes: new[] { Val("Lab") }));

        var messages = report.Warnings.Where(w => w.Kind == K.NearMiss).Select(w => w.Message).OrderBy(m => m).ToArray();
        Assert.Equal(new[]
        {
            "Will add campus Chilliwak. Chilliwack already exists (row 2).",
            "Will add meeting type Labb. Lab already exists (row 2).",
            "Will add section type Lec. Lecture already exists (row 2).",
            "Will add subject CHEMS. CHEM already exists (row 2).",
        }, messages);
    }

    /// <summary>A new building close to an existing building is a warning (SCIENCE versus SCI).</summary>
    [Fact]
    public void NearMiss_NewBuilding_VersusExisting()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Building, "SCIENCE"), (C.Room, "210"))),
            Snapshot(rooms: new[] { Rm("SCI", "100") }));

        var warning = Assert.Single(report.Warnings.Where(w => w.Kind == K.NearMiss));
        Assert.Equal("Will add building SCIENCE. SCI already exists (row 2).", warning.Message);
    }

    /// <summary>A new building with no near miss, and a new room in an existing building, are not warned about.</summary>
    [Fact]
    public void NearMiss_NewBuildingWithNoNearMiss_AndNewRoomInExistingBuilding_NoWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Building, "ZED"), (C.Room, "210")),
                Mtg(3, "CHEM", "101", "B", "M", "1000", "1100", (C.Building, "SCI"), (C.Room, "211"))),
            Snapshot(rooms: new[] { Rm("SCI", "100") }));

        Assert.DoesNotContain(report.Warnings, w => w.Kind == K.NearMiss);
    }

    /// <summary>Two new buildings close to each other are one warning, reported once.</summary>
    [Fact]
    public void NearMiss_TwoNewBuildings()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Building, "SCI"), (C.Room, "1")),
                Mtg(3, "CHEM", "101", "B", "M", "1000", "1100", (C.Building, "SCIENCE"), (C.Room, "2"))),
            Snapshot());

        var warning = Assert.Single(report.Warnings.Where(w => w.Kind == K.NearMiss));
        Assert.Equal("Will add buildings SCI and SCIENCE. Check they're meant to be different (rows 2 and 3).", warning.Message);
    }

    // ── A room's campus ───────────────────────────────────────────────────────

    /// <summary>A room on one campus used by sections on another is one warning per room and pair of campuses.</summary>
    [Fact]
    public void RoomCampus_Differs_IsGroupedByRoom()
    {
        var abbotsford = Camp("Abbotsford");
        var chilliwack = Camp("Chilliwack");
        var room = Rm("SCI", "210");
        room.CampusId = abbotsford.Id;

        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Campus, "Chilliwack"), (C.Building, "SCI"), (C.Room, "210")),
                Mtg(3, "CHEM", "101", "B", "T", "1000", "1100", (C.Campus, "Chilliwack"), (C.Building, "SCI"), (C.Room, "210"))),
            Snapshot(campuses: new[] { abbotsford, chilliwack }, rooms: new[] { room }));

        var warning = Assert.Single(Of(report, K.RoomCampus));
        Assert.Equal(new[] { 2, 3 }, warning.Rows);
        Assert.Equal("Room SCI 210 is on the Abbotsford campus, but its sections here are on Chilliwack (rows 2 and 3).",
            warning.Message);
    }

    /// <summary>No warning when the campuses match, or when either one is not set.</summary>
    [Fact]
    public void RoomCampus_SameOrUnset_NoWarning()
    {
        var abbotsford = Camp("Abbotsford");
        var onCampus = Rm("SCI", "210");
        onCampus.CampusId = abbotsford.Id;
        var noCampus = Rm("SCI", "211");

        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Campus, "Abbotsford"), (C.Building, "SCI"), (C.Room, "210")),
                Mtg(3, "CHEM", "101", "B", "M", "1000", "1100", (C.Campus, "Abbotsford"), (C.Building, "SCI"), (C.Room, "211")),
                Mtg(4, "CHEM", "101", "C", "M", "1000", "1100", (C.Building, "SCI"), (C.Room, "210"))),
            Snapshot(campuses: new[] { abbotsford }, rooms: new[] { onCampus, noCampus }));

        Assert.Empty(Of(report, K.RoomCampus));
    }

    /// <summary>A section on a campus the file adds is named by its new name.</summary>
    [Fact]
    public void RoomCampus_SectionOnANewCampus_IsNamed()
    {
        var abbotsford = Camp("Abbotsford");
        var room = Rm("SCI", "210");
        room.CampusId = abbotsford.Id;

        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Campus, "Hope"), (C.Building, "SCI"), (C.Room, "210"))),
            Snapshot(campuses: new[] { abbotsford }, rooms: new[] { room }));

        Assert.Equal("Room SCI 210 is on the Abbotsford campus, but its sections here are on Hope (row 2).",
            Assert.Single(Of(report, K.RoomCampus)).Message);
    }

    // ── Overlapping meetings ──────────────────────────────────────────────────

    /// <summary>Two weekly meetings that overlap on a day are one warning for the section, naming the day and rows.</summary>
    [Fact]
    public void Overlap_Weekly_IsOneWarningPerSection()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1200"),
                Mtg(3, "CHEM", "101", "A", "M,W", "1100", "1300")),
            Snapshot());

        var warning = Assert.Single(Of(report, K.OverlappingMeetings));
        Assert.Equal(new[] { 2, 3 }, warning.Rows);
        Assert.Equal("CHEM101 A has meetings at overlapping times on Monday (rows 2 and 3).", warning.Message);
    }

    /// <summary>One section whose meetings overlap on several days is still one warning, naming the days.</summary>
    [Fact]
    public void Overlap_SeveralDays_AreNamedInOneWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M,W", "1000", "1200"),
                Mtg(3, "CHEM", "101", "A", "M,W", "1100", "1300")),
            Snapshot());

        Assert.Equal("CHEM101 A has meetings at overlapping times on Monday and Wednesday (rows 2 and 3).",
            Assert.Single(Of(report, K.OverlappingMeetings)).Message);
    }

    /// <summary>Two sections that each overlap get a warning each.</summary>
    [Fact]
    public void Overlap_TwoSections_GetOneWarningEach()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1200"),
                Mtg(3, "CHEM", "101", "A", "M", "1100", "1300"),
                Mtg(4, "CHEM", "101", "B", "T", "1000", "1200"),
                Mtg(5, "CHEM", "101", "B", "T", "1000", "1200")),
            Snapshot());

        Assert.Equal(2, Of(report, K.OverlappingMeetings).Count);
    }

    /// <summary>Meetings that merely touch, or are on different days, do not overlap.</summary>
    [Fact]
    public void Overlap_TouchingOrDifferentDays_NoWarning()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100"),
                Mtg(3, "CHEM", "101", "A", "M", "1100", "1200"),
                Mtg(4, "CHEM", "101", "A", "T", "1000", "1200")),
            Snapshot());

        Assert.Empty(Of(report, K.OverlappingMeetings));
    }

    /// <summary>Whether two meetings at the same time overlap depends on whether they share a week.</summary>
    [Theory]
    [InlineData("odd", "even", false)]
    [InlineData("even", "odd", false)]
    [InlineData("odd", "odd", true)]
    [InlineData("even", "even", true)]
    [InlineData("", "odd", true)]
    [InlineData("even", "", true)]
    [InlineData("odd", "2,4", false)]
    [InlineData("odd", "2,3", true)]
    [InlineData("even", "1,3", false)]
    [InlineData("even", "3,4", true)]
    [InlineData("1,3", "2,4", false)]
    [InlineData("1,3", "3,5", true)]
    [InlineData("odd", "21", true)]
    public void Overlap_DependsOnSharingAWeek(string first, string second, bool warns)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1200", (C.Frequency, first)),
                Mtg(3, "CHEM", "101", "A", "M", "1000", "1200", (C.Frequency, second))),
            Snapshot());

        Assert.Empty(report.Problems);
        Assert.Equal(warns ? 1 : 0, Of(report, K.OverlappingMeetings).Count);
    }

    // ── Order, and skipped sections ───────────────────────────────────────────

    /// <summary>File-wide warnings come first, then the rest by the first row each affects.</summary>
    [Fact]
    public void Order_FileWideFirst_ThenByFirstRow()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "Intro");
        course.IsActive = false;

        // No start times (file-wide), an inactive course on row 2, a near-miss tag on row 3.
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(3, "CHEM", "102", "A", "M", "1000", "1100", (C.Tags, "Writng")),
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100")),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }, tags: new[] { Val("Writing") }));

        Assert.Equal(new[] { K.NoLegalStartTimes, K.Inactive, K.NearMiss }, report.Warnings.Select(w => w.Kind).ToArray());
    }

    /// <summary>Warnings about a skipped section's non-key cells are never produced.</summary>
    [Fact]
    public void SkippedSection_ProducesNoWarnings()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "General Chemistry");
        course.IsActive = false;

        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "1045", "1215", (C.CourseTitle, "Other"), (C.Tags, "Writng"))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }, sections: new[] { Sect(course, "A") },
                tags: new[] { Val("Writing") }, legalStartTimes: StartTimes()));

        Assert.Empty(report.Warnings);
        Assert.Empty(report.Problems);
    }
}
