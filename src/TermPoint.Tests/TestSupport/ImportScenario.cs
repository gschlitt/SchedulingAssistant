using TermPoint.Models;
using TermPoint.Services.SectionImport;
using TermPoint.ViewModels.Management;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// The standard scenario for the section import writer and service tests: some existing records in the
/// database, and one sheet that matches some of them and creates others.
/// </summary>
public static class ImportScenario
{
    /// <summary>The records <see cref="SeedExisting"/> inserts.</summary>
    /// <param name="Chem">Subject CHEM.</param>
    /// <param name="Chem101">Course CHEM101: level 100, capacity 40, tagged Writing.</param>
    /// <param name="Smith">Instructor Smith, John (JS).</param>
    /// <param name="Lecture">Section type Lecture.</param>
    /// <param name="ClassMeeting">Meeting type Class.</param>
    /// <param name="Writing">Tag Writing.</param>
    /// <param name="Main">Campus Main.</param>
    /// <param name="Sci210">Room SCI 210 (no campus).</param>
    public sealed record Existing(
        Subject Chem, Course Chem101, Instructor Smith, SchedulingEnvironmentValue Lecture,
        SchedulingEnvironmentValue ClassMeeting, SchedulingEnvironmentValue Writing, Campus Main, Room Sci210);

    /// <summary>
    /// Inserts the existing records, and one existing section (CHEM101 Z) so the semester is not empty.
    /// </summary>
    /// <param name="rig">The database to seed.</param>
    /// <returns>The records inserted.</returns>
    public static Existing SeedExisting(ImportRig rig)
    {
        var chem = new Subject { Name = "Chemistry", CalendarAbbreviation = "CHEM" };
        rig.Subjects.Insert(chem);

        var writing = new SchedulingEnvironmentValue { Name = "Writing", SortOrder = 1 };
        rig.Environment.Insert(SchedulingEnvironmentTypes.Tag, writing);
        var lecture = new SchedulingEnvironmentValue { Name = "Lecture", SortOrder = 1 };
        rig.Environment.Insert(SchedulingEnvironmentTypes.SectionType, lecture);
        var classMeeting = new SchedulingEnvironmentValue { Name = "Class", SortOrder = 1 };
        rig.Environment.Insert(SchedulingEnvironmentTypes.MeetingType, classMeeting);

        var main = new Campus { Name = "Main", SortOrder = 1 };
        rig.Campuses.Insert(main);
        var sci210 = new Room { Building = "SCI", RoomNumber = "210", SortOrder = 1 };
        rig.Rooms.Insert(sci210);

        var chem101 = new Course
        {
            SubjectId = chem.Id, CalendarCode = "CHEM101", Title = "Intro to Chemistry",
            Level = "100", Capacity = 40, TagIds = { writing.Id },
        };
        rig.Courses.Insert(chem101);
        var smith = new Instructor { LastName = "Smith", FirstName = "John", Initials = "JS" };
        rig.Instructors.Insert(smith);

        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = chem101.Id, SectionCode = "Z" });

        return new Existing(chem, chem101, smith, lecture, classMeeting, writing, main, sci210);
    }

    /// <summary>
    /// The standard sheet (the five rows 2 to 5). Against <see cref="SeedExisting"/> it creates a subject
    /// (BIOL), a section type (Seminar), a meeting type (Studio), a tag (Lab Safety), a room (ART 101, in
    /// campus Main), a course (BIOL200), an instructor (Jones, Mary) and three sections: CHEM101 A on two
    /// rows (three meetings), BIOL200 B with no meetings, and CHEM101 C with nothing but its key.
    /// </summary>
    /// <returns>The sheet.</returns>
    public static ImportSheet Sheet() => SectionImportTestKit.Sheet(
        Mtg(2, "CHEM", "101", "A", "M,W", "1000", "1100",
            (C.SectionType, "Lecture"), (C.Instructors, "Smith, John; Jones, Mary"),
            (C.Tags, "Writing; Lab Safety"), (C.Campus, "Main"),
            (C.MeetingType, "Class"), (C.Building, "SCI"), (C.Room, "210")),
        Mtg(3, "CHEM", "101", "A", "T", "1300", "1450",
            (C.Frequency, "odd"), (C.MeetingType, "Studio"), (C.Building, "ART"), (C.Room, "101")),
        Sec(4, "BIOL", "200", "B",
            (C.CourseTitle, "Genetics"), (C.SectionType, "Seminar"), (C.Capacity, "25"),
            (C.Instructors, "Jones, Mary")),
        Sec(5, "CHEM", "101", "C"));
}
