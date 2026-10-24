using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the second half of <see cref="SectionImportPlanner"/>'s step 24.3: resolving the values a
/// new section names (section type, campus, tags, instructors, meeting types, rooms) to an existing
/// record, a single shared new record, or a problem. Pure: sheets and snapshots are built by hand.
/// </summary>
public class SectionImportPlannerValueTests
{
    // ── Section type and campus (matched by name; blank = none) ───────────────

    /// <summary>A section type matches an existing one by name, ignoring case and spacing.</summary>
    [Fact]
    public void SectionType_Existing_IsMatchedByName()
    {
        var lecture = Val("Lecture");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.SectionType, " lecture "))),
            Snapshot(sectionTypes: new[] { lecture }));

        Assert.Empty(report.Problems);
        Assert.Empty(report.Plan.NewSectionTypes);
        var type = report.Plan.NewSections.Single().SectionType!;
        Assert.Same(lecture, type.Record);
        Assert.False(type.IsNew);
    }

    /// <summary>An unknown section type is planned, named as first typed, and shared by every section that names it.</summary>
    [Fact]
    public void SectionType_New_IsNamedAsFirstTyped()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.SectionType, "Seminar")), Sec(3, "CHEM", "101", "B", (C.SectionType, "SEMINAR"))),
            Snapshot());

        var type = Assert.Single(report.Plan.NewSectionTypes);
        Assert.True(type.IsNew);
        Assert.Equal("Seminar", type.Record.Name);
        Assert.Equal(2, type.FirstRow);
        Assert.All(report.Plan.NewSections, s => Assert.Same(type, s.SectionType));
    }

    /// <summary>A blank section type is null, and nothing is planned.</summary>
    [Fact]
    public void SectionType_Blank_IsNull()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.Null(report.Plan.NewSections.Single().SectionType);
        Assert.Empty(report.Plan.NewSectionTypes);
    }

    /// <summary>The section's type comes from the first row where it is filled, even a later one.</summary>
    [Fact]
    public void SectionLevelValue_ComesFromTheFirstNonBlankRow()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A"),
                Sec(3, "CHEM", "101", "A", (C.SectionType, "Lab"), (C.Capacity, "30")),
                Sec(4, "CHEM", "101", "A", (C.SectionType, "Other"), (C.Capacity, "40"))),
            Snapshot());

        var draft = report.Plan.NewSections.Single();
        Assert.Equal("Lab", draft.SectionType!.Record.Name);
        Assert.Equal("30", draft.RawCapacity);
        Assert.Equal(new[] { 2, 3, 4 }, draft.CellsByRow(C.CourseTitle).Select(c => c.Row).ToArray());
    }

    /// <summary>Two section types with the same name cannot be told apart: one problem on the row that supplied the name.</summary>
    [Fact]
    public void SectionType_Ambiguous_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "101", "A", (C.SectionType, "Lecture"))),
            Snapshot(sectionTypes: new[] { Val("Lecture"), Val("lecture") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(3, problem.Row);
        Assert.Equal(C.SectionType, problem.Column);
        Assert.Equal("There are two section types called Lecture in TermPoint.", problem.Message);
    }

    /// <summary>A campus is matched by name, planned when new, and a duplicate name is a problem.</summary>
    [Fact]
    public void Campus_ExistingNewAndAmbiguous()
    {
        var main = Camp("Main");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Campus, "main")),
                Sec(3, "CHEM", "101", "B", (C.Campus, "North")),
                Sec(4, "CHEM", "101", "C", (C.Campus, "Twin"))),
            Snapshot(campuses: new[] { main, Camp("Twin"), Camp("TWIN") }));

        Assert.Same(main, report.Plan.NewSections[0].Campus!.Record);
        Assert.Equal("North", Assert.Single(report.Plan.NewCampuses).Record.Name);
        Assert.Same(report.Plan.NewCampuses[0], report.Plan.NewSections[1].Campus);

        var problem = Assert.Single(report.Problems);
        Assert.Equal(4, problem.Row);
        Assert.Equal(C.Campus, problem.Column);
        Assert.Equal("There are two campuses called Twin in TermPoint.", problem.Message);
        Assert.Null(report.Plan.NewSections[2].Campus);
    }

    // ── Meeting type ──────────────────────────────────────────────────────────

    /// <summary>A meeting type is matched, planned when new, blank is null, and a duplicate name is a problem.</summary>
    [Fact]
    public void MeetingType_ExistingNewBlankAndAmbiguous()
    {
        var lecture = Val("Lecture");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.MeetingType, "LECTURE")),
                Sec(3, "CHEM", "101", "A", (C.MeetingType, "Tutorial")),
                Sec(4, "CHEM", "101", "A"),
                Sec(5, "CHEM", "101", "A", (C.MeetingType, "Lab"))),
            Snapshot(meetingTypes: new[] { lecture, Val("Lab"), Val("lab") }));

        var rows = report.Plan.NewSections.Single().MeetingRows;
        Assert.Same(lecture, rows[0].MeetingType!.Record);
        Assert.Equal("Tutorial", rows[1].MeetingType!.Record.Name);
        Assert.True(rows[1].MeetingType!.IsNew);
        Assert.Null(rows[2].MeetingType);
        Assert.Null(rows[3].MeetingType);
        Assert.Equal("Tutorial", Assert.Single(report.Plan.NewMeetingTypes).Record.Name);

        var problem = Assert.Single(report.Problems);
        Assert.Equal(5, problem.Row);
        Assert.Equal(C.MeetingType, problem.Column);
        Assert.Equal("There are two meeting types called Lab in TermPoint.", problem.Message);
    }

    // ── Tags ──────────────────────────────────────────────────────────────────

    /// <summary>Tags split on semicolons; each is matched or planned, in the order listed, and empty entries are ignored.</summary>
    [Fact]
    public void Tags_AreSplitMatchedAndPlanned()
    {
        var writing = Val("Writing");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, " writing ;; Lab ; "))),
            Snapshot(tags: new[] { writing }));

        Assert.Empty(report.Problems);
        var tags = report.Plan.NewSections.Single().Tags;
        Assert.Equal(2, tags.Count);
        Assert.Same(writing, tags[0].Record);
        Assert.Equal("Lab", tags[1].Record.Name);
        Assert.True(tags[1].IsNew);
        Assert.Same(tags[1], Assert.Single(report.Plan.NewTags));
    }

    /// <summary>A tag listed twice in one cell is a problem, reported once.</summary>
    [Fact]
    public void Tags_ListedTwiceInOneCell_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Lab; lab ; Writing; LAB"))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Tags, problem.Column);
        Assert.Equal("Lab is listed twice in Tags.", problem.Message);
        Assert.Equal(2, report.Plan.NewSections.Single().Tags.Count);
    }

    /// <summary>An ambiguous tag is one problem (a fault in TermPoint's data), however many sections name it.</summary>
    [Fact]
    public void Tags_Ambiguous_IsOneProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Lab; Writing")), Sec(3, "CHEM", "101", "B", (C.Tags, "Lab"))),
            Snapshot(tags: new[] { Val("Lab"), Val("LAB") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal("There are two tags called Lab in TermPoint.", problem.Message);
        Assert.Equal("Writing", report.Plan.NewSections[0].Tags.Single().Record.Name);
    }

    // ── Rooms ─────────────────────────────────────────────────────────────────

    /// <summary>Both blank: no room, no problem.</summary>
    [Fact]
    public void Room_BothBlank_IsNoRoom()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.Empty(report.Problems);
        Assert.Null(report.Plan.NewSections.Single().MeetingRows.Single().Room);
    }

    /// <summary>A building with no room is a problem on that row.</summary>
    [Fact]
    public void Room_BuildingWithoutRoom_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Building, "SCI"))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Room, problem.Column);
        Assert.Equal("Building SCI has no Room.", problem.Message);
    }

    /// <summary>A room number alone matches the one existing room that has it, in any building.</summary>
    [Fact]
    public void Room_NumberAlone_MatchesTheOnlyRoomWithThatNumber()
    {
        var room = Rm("SCI", "210");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Room, "210"))),
            Snapshot(rooms: new[] { room, Rm("SCI", "211") }));

        Assert.Empty(report.Problems);
        Assert.Same(room, report.Plan.NewSections.Single().MeetingRows.Single().Room!.Record);
        Assert.Empty(report.Plan.NewRooms);
    }

    /// <summary>A room number alone that several buildings have is a problem naming the buildings.</summary>
    [Fact]
    public void Room_NumberAlone_InSeveralBuildings_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Room, "210"))),
            Snapshot(rooms: new[] { Rm("SCI", "210"), Rm("ENG", "210") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Building, problem.Column);
        Assert.Equal("Room 210 is in more than one building (SCI, ENG). Fill in the Building.", problem.Message);
    }

    /// <summary>A room number alone that matches no existing room is a problem (a new room needs a building too).</summary>
    [Fact]
    public void Room_NumberAlone_MatchingNothing_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Room, "210"))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Building, problem.Column);
        Assert.Equal("Room 210 isn't in TermPoint yet. To add it, give its building too.", problem.Message);
        Assert.Empty(report.Plan.NewRooms);
    }

    /// <summary>
    /// A room number alone never matches a room that this same import plans: only rooms already in
    /// TermPoint count, so the result does not depend on row order.
    /// </summary>
    [Fact]
    public void Room_NumberAlone_DoesNotMatchANewRoomOfTheSameImport()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Building, "SCI"), (C.Room, "210")),
                Sec(3, "CHEM", "101", "B", (C.Room, "210"))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(3, problem.Row);
    }

    /// <summary>Building and number match an existing room ignoring case and spacing; none plans a new room as first typed.</summary>
    [Fact]
    public void Room_BuildingAndNumber_ExistingOrNew()
    {
        var room = Rm("SCI", "210");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Building, " sci "), (C.Room, "210")),
                Sec(3, "CHEM", "101", "B", (C.Building, "Eng"), (C.Room, "5a"))),
            Snapshot(rooms: new[] { room }));

        Assert.Empty(report.Problems);
        Assert.Same(room, report.Plan.NewSections[0].MeetingRows[0].Room!.Record);

        var created = Assert.Single(report.Plan.NewRooms);
        Assert.Equal("Eng", created.Record.Building);
        Assert.Equal("5a", created.Record.RoomNumber);
        Assert.Null(created.Record.Capacity);
        Assert.Null(created.Record.RoomTypeId);
        Assert.Same(created, report.Plan.NewSections[1].MeetingRows[0].Room);
    }

    /// <summary>Two existing rooms with the same building and number are a problem, reported once.</summary>
    [Fact]
    public void Room_BuildingAndNumber_Ambiguous_IsOneProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Building, "SCI"), (C.Room, "210")),
                Sec(3, "CHEM", "101", "B", (C.Building, "SCI"), (C.Room, "210"))),
            Snapshot(rooms: new[] { Rm("SCI", "210"), Rm("sci", "210") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Room, problem.Column);
        Assert.Equal("There are two rooms called SCI 210 in TermPoint.", problem.Message);
    }

    /// <summary>A new room's campus is the campus of every new section meeting in it, when they all agree.</summary>
    [Fact]
    public void NewRoomCampus_IsSetWhenAllSectionsAgree()
    {
        var main = Camp("Main");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Building, "SCI"), (C.Room, "210"), (C.Campus, "Main")),
                Sec(3, "CHEM", "101", "B", (C.Building, "SCI"), (C.Room, "210"), (C.Campus, "main")),
                Sec(4, "CHEM", "101", "C", (C.Building, "NEW"), (C.Room, "1"), (C.Campus, "Fresh")),
                Sec(5, "CHEM", "101", "D", (C.Building, "NEW"), (C.Room, "1"), (C.Campus, "Fresh"))),
            Snapshot(campuses: new[] { main }));

        Assert.Empty(report.Problems);
        Assert.Equal(main.Id, report.Plan.NewRooms[0].Record.CampusId);
        Assert.Equal(report.Plan.NewCampuses.Single().Record.Id, report.Plan.NewRooms[1].Record.CampusId);
    }

    /// <summary>A new room stays without a campus when its sections name different campuses, or one names none.</summary>
    [Fact]
    public void NewRoomCampus_IsNullWhenSectionsDisagree()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Building, "SCI"), (C.Room, "210"), (C.Campus, "Main")),
                Sec(3, "CHEM", "101", "B", (C.Building, "SCI"), (C.Room, "210"), (C.Campus, "North")),
                Sec(4, "CHEM", "101", "C", (C.Building, "ENG"), (C.Room, "5"), (C.Campus, "Main")),
                Sec(5, "CHEM", "101", "D", (C.Building, "ENG"), (C.Room, "5"))),
            Snapshot());

        Assert.Empty(report.Problems);
        Assert.All(report.Plan.NewRooms, r => Assert.Null(r.Record.CampusId));
    }

    /// <summary>A skipped section's campus does not count towards a new room's campus.</summary>
    [Fact]
    public void NewRoomCampus_IgnoresSkippedSections()
    {
        var chem = Subj("CHEM");
        var course = Crs(chem, "CHEM101");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Building, "SCI"), (C.Room, "210"), (C.Campus, "North")),
                Sec(3, "CHEM", "101", "B", (C.Building, "SCI"), (C.Room, "210"), (C.Campus, "Main"))),
            Snapshot(subjects: new[] { chem }, courses: new[] { course }, sections: new[] { Sect(course, "A") }));

        Assert.Equal(report.Plan.NewCampuses.Single().Record.Id, report.Plan.NewRooms.Single().Record.CampusId);
        Assert.Equal("Main", report.Plan.NewCampuses.Single().Record.Name);
    }

    // ── Instructors ───────────────────────────────────────────────────────────

    /// <summary>"Last, First; Last, First" makes two new instructors, in file order, active, with initials.</summary>
    [Fact]
    public void Instructors_SeveralNewOnes_AreBuiltInOrder()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John; Lee, Ann"))), Snapshot());

        Assert.Empty(report.Problems);
        var listed = report.Plan.NewSections.Single().Instructors;
        Assert.Equal(new[] { "Smith", "Lee" }, listed.Select(i => i.Record.LastName).ToArray());
        Assert.Equal(new[] { "John", "Ann" }, listed.Select(i => i.Record.FirstName).ToArray());
        Assert.Equal(new[] { "JS", "AL" }, listed.Select(i => i.Record.Initials).ToArray());
        Assert.All(listed, i => Assert.True(i.IsNew && i.Record.IsActive));
        Assert.Equal(listed, report.Plan.NewInstructors);
    }

    /// <summary>An existing instructor matches on both names, ignoring case and spacing, and is not re-created.</summary>
    [Fact]
    public void Instructors_Existing_AreMatchedOnBothNames()
    {
        var john = Inst("Smith", "John", "JS");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, " smith ,  JOHN "))),
            Snapshot(instructors: new[] { john, Inst("Smith", "Jane", "JS2") }));

        Assert.Empty(report.Problems);
        Assert.Empty(report.Plan.NewInstructors);
        var matched = report.Plan.NewSections.Single().Instructors.Single();
        Assert.Same(john, matched.Record);
        Assert.False(matched.IsNew);
    }

    /// <summary>An entry with no comma is a last name only.</summary>
    [Fact]
    public void Instructors_LastNameOnly_HasAnEmptyFirstName()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith"))), Snapshot());

        var instructor = Assert.Single(report.Plan.NewInstructors).Record;
        Assert.Equal("Smith", instructor.LastName);
        Assert.Equal("", instructor.FirstName);
        Assert.Equal("SM", instructor.Initials);
    }

    /// <summary>The entry is split at the first comma only.</summary>
    [Fact]
    public void Instructors_SplitAtTheFirstComma()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John, Jr."))), Snapshot());

        var instructor = Assert.Single(report.Plan.NewInstructors).Record;
        Assert.Equal("Smith", instructor.LastName);
        Assert.Equal("John, Jr.", instructor.FirstName);
    }

    /// <summary>A missing last name is a problem, and the other entries are still resolved.</summary>
    [Fact]
    public void Instructors_WithoutALastName_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, ", John; Lee, Ann"))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Instructors, problem.Column);
        Assert.Equal("Each instructor needs a last name, written Last, First.", problem.Message);
        Assert.Equal("Lee", Assert.Single(report.Plan.NewInstructors).Record.LastName);
    }

    /// <summary>Two existing instructors with the same name are a problem naming the person.</summary>
    [Fact]
    public void Instructors_Ambiguous_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John"))),
            Snapshot(instructors: new[] { Inst("Smith", "John", "JS"), Inst("smith", "john", "JS2") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal("There are two instructors called Smith, John in TermPoint.", problem.Message);
    }

    /// <summary>The same person twice in one cell is a problem.</summary>
    [Fact]
    public void Instructors_SamePersonTwiceInOneCell_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John; smith,john"))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(C.Instructors, problem.Column);
        Assert.Equal("Smith, John is listed twice in Instructor(s).", problem.Message);
        Assert.Single(report.Plan.NewSections.Single().Instructors);
    }

    /// <summary>One new instructor per normalised name across the file, shared by every section.</summary>
    [Fact]
    public void Instructors_SameNewPersonOnManySections_IsOneRecord()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John")),
                Sec(3, "CHEM", "101", "B", (C.Instructors, "SMITH,  John; Lee, Ann")),
                Sec(4, "CHEM", "101", "C", (C.Instructors, "Lee, Ann"))),
            Snapshot());

        Assert.Equal(2, report.Plan.NewInstructors.Count);
        var smith = report.Plan.NewInstructors[0];
        var lee = report.Plan.NewInstructors[1];
        Assert.Same(smith, report.Plan.NewSections[0].Instructors[0]);
        Assert.Same(smith, report.Plan.NewSections[1].Instructors[0]);
        Assert.Same(lee, report.Plan.NewSections[1].Instructors[1]);
        Assert.Same(lee, report.Plan.NewSections[2].Instructors[0]);
        Assert.Equal("John", smith.Record.FirstName);
        Assert.Equal(2, smith.FirstRow);
    }

    /// <summary>Two new instructors with the same initials get distinct ones: JS, JS2.</summary>
    [Fact]
    public void Instructors_NewOnesWithCollidingInitials_GetDistinctInitials()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John; Stone, Jane; Short, Jim"))), Snapshot());

        Assert.Equal(new[] { "JS", "JS2", "JS3" }, report.Plan.NewInstructors.Select(i => i.Record.Initials).ToArray());
    }

    /// <summary>A new instructor whose initials collide with an existing instructor's gets the next free ones.</summary>
    [Fact]
    public void Instructors_NewWithInitialsOfAnExistingOne_GetNextFree()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John"))),
            Snapshot(instructors: new[] { Inst("Stone", "Jane", "js") }));

        Assert.Equal("JS2", Assert.Single(report.Plan.NewInstructors).Record.Initials);
    }

    // ── Shared instances and sort order ───────────────────────────────────────

    /// <summary>
    /// A new value named on ten sections is planned once, and all ten drafts hold the same instance
    /// (same Id), for every kind of record.
    /// </summary>
    [Fact]
    public void ANewValueNamedOnTenRows_IsOnePlanEntryAndOneSharedInstance()
    {
        var rows = Enumerable.Range(0, 10).Select(i => Sec(2 + i, "CHEM", "101", $"S{i}",
            (C.SectionType, "Seminar"), (C.Campus, "North"), (C.Tags, "Writing"),
            (C.Instructors, "Smith, John"), (C.MeetingType, "Tutorial"),
            (C.Building, "SCI"), (C.Room, "210"))).ToArray();

        var report = SectionImportPlanner.Plan(Sheet(rows), Snapshot());

        Assert.Empty(report.Problems);
        var plan = report.Plan;
        Assert.Equal(10, plan.NewSections.Count);
        Assert.Single(plan.NewSubjects);
        Assert.Single(plan.NewCourses);
        Assert.Single(plan.NewSectionTypes);
        Assert.Single(plan.NewCampuses);
        Assert.Single(plan.NewTags);
        Assert.Single(plan.NewInstructors);
        Assert.Single(plan.NewMeetingTypes);
        Assert.Single(plan.NewRooms);
        Assert.All(plan.NewSections, s =>
        {
            Assert.Same(plan.NewCourses[0], s.Course);
            Assert.Same(plan.NewSectionTypes[0], s.SectionType);
            Assert.Same(plan.NewCampuses[0], s.Campus);
            Assert.Same(plan.NewTags[0], s.Tags.Single());
            Assert.Same(plan.NewInstructors[0], s.Instructors.Single());
            Assert.Same(plan.NewMeetingTypes[0], s.MeetingRows.Single().MeetingType);
            Assert.Same(plan.NewRooms[0], s.MeetingRows.Single().Room);
        });
    }

    /// <summary>An existing value named on many rows is also one shared reference.</summary>
    [Fact]
    public void AnExistingValueNamedOnManyRows_IsOneSharedReference()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Lab")), Sec(3, "CHEM", "101", "B", (C.Tags, "lab"))),
            Snapshot(tags: new[] { Val("Lab") }));

        Assert.Same(report.Plan.NewSections[0].Tags[0], report.Plan.NewSections[1].Tags[0]);
    }

    /// <summary>
    /// New setup values continue from the existing maximum SortOrder, in order of first appearance;
    /// a kind with none starts at 0.
    /// </summary>
    [Fact]
    public void SortOrder_ContinuesFromTheExistingMaximum_InOrderOfFirstAppearance()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Tags, "Zeta; Alpha"), (C.SectionType, "New type"),
                    (C.Campus, "Zed"), (C.MeetingType, "Tut"), (C.Building, "B1"), (C.Room, "1")),
                Sec(3, "CHEM", "101", "B", (C.Tags, "Mid; Zeta"), (C.SectionType, "Another"),
                    (C.Campus, "Ann"), (C.MeetingType, "Prac"), (C.Building, "B2"), (C.Room, "2"))),
            Snapshot(
                tags: new[] { Val("Old", 7), Val("Older", 3) },
                sectionTypes: new[] { Val("Existing", 4) },
                campuses: new[] { Camp("Main", 2) },
                rooms: new[] { Rm("SCI", "100", 9), Rm("SCI", "101", 1) }));

        Assert.Empty(report.Problems);
        var plan = report.Plan;
        Assert.Equal(new[] { "Zeta", "Alpha", "Mid" }, plan.NewTags.Select(t => t.Record.Name).ToArray());
        Assert.Equal(new[] { 8, 9, 10 }, plan.NewTags.Select(t => t.Record.SortOrder).ToArray());
        Assert.Equal(new[] { 5, 6 }, plan.NewSectionTypes.Select(t => t.Record.SortOrder).ToArray());
        Assert.Equal(new[] { 3, 4 }, plan.NewCampuses.Select(t => t.Record.SortOrder).ToArray());
        Assert.Equal(new[] { 0, 1 }, plan.NewMeetingTypes.Select(t => t.Record.SortOrder).ToArray());
        Assert.Equal(new[] { 10, 11 }, plan.NewRooms.Select(t => t.Record.SortOrder).ToArray());
    }
}
