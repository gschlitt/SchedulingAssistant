using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// A titled list of display lines, for an expandable list in the Ready-to-import panel: "Subjects" with
/// its new subjects, and so on.
/// </summary>
/// <param name="Heading">The list's heading, for example "Rooms".</param>
/// <param name="Items">The display lines.</param>
public sealed record ImportListGroup(string Heading, IReadOnlyList<string> Items)
{
    /// <summary>How many lines the group holds (for a heading such as "Rooms (14)").</summary>
    public int Count => Items.Count;
}

/// <summary>
/// The wording of the Import Sections flyout: every sentence built from counts and names, and the display
/// line of each new record. Pure functions with no window and no database, so the exact text, plurals
/// included, is unit-tested. Every message names what happened and what it means for the user's work, and
/// holds no path, exception text or internal term (rule 4 of the shared-database rules).
/// </summary>
public static class ImportMessages
{
    /// <summary>The heading above a list of problems.</summary>
    public const string ProblemsHeading = "This file can't be imported yet. Nothing has been changed.";

    /// <summary>The line above the new result when the database changed between Check and Import (decision D4).</summary>
    public const string ChangedNotice =
        "Something in TermPoint changed since this file was checked. Here is the up-to-date result.";

    /// <summary>The line shown when the semester shown in TermPoint changed while a result was on screen.</summary>
    public const string SemesterChangedNotice =
        "The open semester changed, so that result no longer applies. Nothing has been changed. Choose the file again.";

    /// <summary>Shown when the file is read but has no sections in it.</summary>
    public const string NoSectionsInFile = "This file has no sections to add.";

    /// <summary>Shown when the import failed for a reason other than a refused read-only write.</summary>
    public const string ImportFailed = "The sections couldn't be added. Nothing was changed.";

    /// <summary>Shown when the file could not be checked because of a failure inside TermPoint.</summary>
    public const string CheckFailed =
        "TermPoint couldn't check this file. Nothing has been changed. Try again, and restart TermPoint if it keeps happening.";

    /// <summary>Shown when the template could not be saved where the user chose.</summary>
    public const string TemplateNotSaved = "TermPoint couldn't save the template there. Try another folder, such as Documents.";

    /// <summary>Confirms the template was saved.</summary>
    public const string TemplateSaved = "Template saved.";

    /// <summary>Confirms the problem list is on the clipboard.</summary>
    public const string ListCopied = "Copied.";

    /// <summary>Shown when the list could not be put on the clipboard.</summary>
    public const string ListNotCopied = "TermPoint couldn't copy the list. Nothing has been changed. Try again.";

    /// <summary>Said after an import added rooms: new rooms have no capacity or type yet.</summary>
    public const string NewRoomsNeedDetails = "New rooms need their capacity and room type: see Rooms.";

    /// <summary>Said after an import into an empty semester (decision D5): how to take it back.</summary>
    public const string UndoHint =
        "To take it back, switch to another semester, then use Empty semester in Academic Years.";

    /// <summary>The file name the Save dialog suggests for the template.</summary>
    public const string TemplateFileName = "TermPoint Sections Template.xlsx";

    /// <summary>
    /// The target line: "Sections will be added to Fall 2026 (2026-2027)."
    /// </summary>
    /// <param name="term">The semester's name.</param>
    /// <param name="academicYear">The academic year's name.</param>
    /// <returns>The sentence.</returns>
    public static string TargetLine(string term, string academicYear) =>
        $"Sections will be added to {Where(term, academicYear)}.";

    /// <summary>"Fall 2026 (2026-2027)", or just the term when the year's name is unknown.</summary>
    /// <param name="term">The semester's name.</param>
    /// <param name="academicYear">The academic year's name.</param>
    /// <returns>The place as the messages name it.</returns>
    public static string Where(string term, string academicYear) =>
        string.IsNullOrEmpty(academicYear) ? term : $"{term} ({academicYear})";

    /// <summary>
    /// The ready-to-import summary: "Will add 37 sections, 5 new courses and 8 new instructors." Zero
    /// courses and zero instructors are left out; sections are always named. Singular and plural follow the count.
    /// </summary>
    /// <param name="sections">Sections to add.</param>
    /// <param name="courses">New courses to create.</param>
    /// <param name="instructors">New instructors to create.</param>
    /// <returns>The sentence.</returns>
    public static string Summary(int sections, int courses, int instructors)
    {
        var parts = new List<string> { ImportText.Counted(sections, "section", "sections") };
        if (courses > 0) parts.Add(ImportText.Counted(courses, "new course", "new courses"));
        if (instructors > 0) parts.Add(ImportText.Counted(instructors, "new instructor", "new instructors"));
        return $"Will add {ImportText.JoinWithAnd(parts)}.";
    }

    /// <summary>"Already in Fall 2026, not changed: 12 sections."</summary>
    /// <param name="term">The semester's name.</param>
    /// <param name="skipped">How many sections are already in the semester.</param>
    /// <returns>The sentence.</returns>
    public static string Skipped(string term, int skipped) =>
        $"Already in {term}, not changed: {ImportText.Counted(skipped, "section", "sections")}.";

    /// <summary>"Every section in this file is already in Fall 2026 (2026-2027)."</summary>
    /// <param name="term">The semester's name.</param>
    /// <param name="academicYear">The academic year's name.</param>
    /// <returns>The sentence.</returns>
    public static string NothingToAdd(string term, string academicYear) =>
        $"Every section in this file is already in {Where(term, academicYear)}.";

    /// <summary>
    /// The first line of the done message: "Added 37 sections, 5 courses and 8 instructors to Fall 2026
    /// (2026-2027)." Zero courses and zero instructors are left out.
    /// </summary>
    /// <param name="counts">What the write inserted.</param>
    /// <param name="term">The semester's name.</param>
    /// <param name="academicYear">The academic year's name.</param>
    /// <returns>The sentence.</returns>
    public static string Done(ImportCounts counts, string term, string academicYear)
    {
        var parts = new List<string> { ImportText.Counted(counts.Sections, "section", "sections") };
        if (counts.Courses > 0) parts.Add(ImportText.Counted(counts.Courses, "course", "courses"));
        if (counts.Instructors > 0) parts.Add(ImportText.Counted(counts.Instructors, "instructor", "instructors"));
        return $"Added {ImportText.JoinWithAnd(parts)} to {Where(term, academicYear)}.";
    }

    /// <summary>
    /// The second line of the done message, when setup values were created: "Also added 2 subjects, 3 tags
    /// and 14 rooms." Only the kinds that were added are named. When rooms were added the sentence
    /// <see cref="NewRoomsNeedDetails"/> follows on the same line.
    /// </summary>
    /// <param name="counts">What the write inserted.</param>
    /// <returns>The line, or null when no setup value was created.</returns>
    public static string? SetupLine(ImportCounts counts)
    {
        var parts = new List<string>();
        if (counts.Subjects > 0) parts.Add(ImportText.Counted(counts.Subjects, "subject", "subjects"));
        if (counts.Campuses > 0) parts.Add(ImportText.Counted(counts.Campuses, "campus", "campuses"));
        if (counts.SectionTypes > 0) parts.Add(ImportText.Counted(counts.SectionTypes, "section type", "section types"));
        if (counts.MeetingTypes > 0) parts.Add(ImportText.Counted(counts.MeetingTypes, "meeting type", "meeting types"));
        if (counts.Tags > 0) parts.Add(ImportText.Counted(counts.Tags, "tag", "tags"));
        if (counts.Rooms > 0) parts.Add(ImportText.Counted(counts.Rooms, "room", "rooms"));
        if (parts.Count == 0) return null;

        var line = $"Also added {ImportText.JoinWithAnd(parts)}.";
        return counts.Rooms > 0 ? $"{line} {NewRoomsNeedDetails}" : line;
    }

    /// <summary>A new course as listed: "CHEM199 — Intro Chemistry" (just the code when it has no title).</summary>
    /// <param name="course">The course the import will create.</param>
    /// <returns>The display line.</returns>
    public static string CourseLine(Course course) =>
        string.IsNullOrWhiteSpace(course.Title) ? course.CalendarCode : $"{course.CalendarCode} — {course.Title}";

    /// <summary>A new instructor as listed: "Smith, Jon (JS2)".</summary>
    /// <param name="instructor">The instructor the import will create.</param>
    /// <returns>The display line.</returns>
    public static string InstructorLine(Instructor instructor)
    {
        var name = string.IsNullOrWhiteSpace(instructor.FirstName)
            ? instructor.LastName
            : $"{instructor.LastName}, {instructor.FirstName}";
        return string.IsNullOrWhiteSpace(instructor.Initials) ? name : $"{name} ({instructor.Initials})";
    }

    /// <summary>
    /// A new subject as listed under "New in TermPoint": "CHEM (add its full name in Subjects)". A new
    /// subject is created with its abbreviation as its name, so the user is pointed at where to complete it.
    /// </summary>
    /// <param name="subject">The subject the import will create.</param>
    /// <returns>The display line.</returns>
    public static string SubjectLine(Subject subject) =>
        $"{subject.CalendarAbbreviation} (add its full name in Subjects)";

    /// <summary>A new room as listed: "SCI 210".</summary>
    /// <param name="room">The room the import will create.</param>
    /// <returns>The display line.</returns>
    public static string RoomLine(Room room) => $"{room.Building} {room.RoomNumber}".Trim();

    /// <summary>
    /// The "New in TermPoint" lists: the subjects, section types, meeting types, tags, campuses and rooms the
    /// import will create, one group per kind with a heading, in that order. A kind with nothing new is left
    /// out. Always shown when non-empty, because a typo in the file creates a record.
    /// </summary>
    /// <param name="plan">What the import will create.</param>
    /// <returns>The groups; empty when no setup value will be created.</returns>
    public static IReadOnlyList<ImportListGroup> NewInTermPoint(ImportPlan plan)
    {
        var groups = new List<ImportListGroup>();

        void Add(string heading, IEnumerable<string> lines)
        {
            var items = lines.ToList();
            if (items.Count > 0) groups.Add(new ImportListGroup(heading, items));
        }

        Add("Subjects", plan.NewSubjects.Select(s => SubjectLine(s.Record)));
        Add("Section types", plan.NewSectionTypes.Select(s => s.Record.Name));
        Add("Meeting types", plan.NewMeetingTypes.Select(s => s.Record.Name));
        Add("Tags", plan.NewTags.Select(s => s.Record.Name));
        Add("Campuses", plan.NewCampuses.Select(s => s.Record.Name));
        Add("Rooms", plan.NewRooms.Select(s => RoomLine(s.Record)));
        return groups;
    }
}
