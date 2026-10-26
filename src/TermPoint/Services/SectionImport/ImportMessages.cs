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
/// What a Save dialog of the Import Sections flyout needs to know: the two saves it offers (the template
/// workbook and the instructions web page) differ only in these five strings, so one picker serves both
/// (<c>IImportSectionsPlatform.PickFileToSaveAsync</c>, which exists in the desktop build only). The two real requests are
/// <see cref="ImportMessages.TemplateSaveRequest"/> and <see cref="ImportMessages.InstructionsSaveRequest"/>.
/// </summary>
/// <param name="Title">The dialog's title, for example "Save the template".</param>
/// <param name="SuggestedFileName">The file name the dialog offers, for example "TermPoint Sections Template.xlsx".</param>
/// <param name="DefaultExtension">The extension added when the user types none, without the dot, for example "xlsx".</param>
/// <param name="FileTypeName">The file-type choice's label, for example "Excel workbook (*.xlsx)".</param>
/// <param name="Pattern">The file-type choice's pattern, for example "*.xlsx".</param>
public sealed record SaveFileRequest(
    string Title, string SuggestedFileName, string DefaultExtension, string FileTypeName, string Pattern);

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

    /// <summary>The file name the Save dialog suggests for the instructions web page.</summary>
    public const string InstructionsFileName = "TermPoint Section Import Instructions.html";

    /// <summary>Confirms the instructions web page was saved.</summary>
    public const string InstructionsSaved = "Instructions saved.";

    /// <summary>Shown when the instructions web page could not be saved where the user chose.</summary>
    public const string InstructionsNotSaved = "TermPoint couldn't save the instructions there. Try another folder, such as Documents.";

    /// <summary>
    /// Shown when TermPoint could not read its copy of the instructions: in place of the instructions in the
    /// flyout, and as the error when "Save instructions…" can't save them for that reason (another folder would
    /// not help, so the "try another folder" message would mislead). The same text is on the template's
    /// Instructions sheet, so the user is not left without it.
    /// </summary>
    public const string InstructionsUnavailable =
        "TermPoint couldn't open its copy of the instructions. They're also on the Instructions sheet of the template.";

    /// <summary>The label of the button that shows the instructions.</summary>
    public const string ShowInstructions = "Show instructions";

    /// <summary>The label of the button that hides the instructions again.</summary>
    public const string HideInstructions = "Hide instructions";

    /// <summary>The "Save template…" dialog: an Excel workbook named <see cref="TemplateFileName"/>.</summary>
    public static readonly SaveFileRequest TemplateSaveRequest = new(
        "Save the template", TemplateFileName, "xlsx", "Excel workbook (*.xlsx)", "*.xlsx");

    /// <summary>The "Save instructions…" dialog: a web page named <see cref="InstructionsFileName"/>.</summary>
    public static readonly SaveFileRequest InstructionsSaveRequest = new(
        "Save the instructions", InstructionsFileName, "html", "Web page (*.html)", "*.html");

    /// <summary>The first sentence of the target line, whatever is selected: the rule for where sections go.</summary>
    public const string TargetRule =
        "Sections are imported one semester at a time, to the currently selected semester.";

    /// <summary>The target line's second sentence when no semester is selected.</summary>
    public const string NoSemesterSelected = "No semester is selected. Select one to import sections into it.";

    /// <summary>The target line's second sentence when more than one semester is selected.</summary>
    public const string SeveralSemestersSelected =
        "Several semesters are selected. Select just one to import sections into it.";

    /// <summary>
    /// The target line when one semester is selected: "Sections are imported one semester at a time, to the
    /// currently selected semester. The current semester is Fall 2026 (2026-2027)."
    /// </summary>
    /// <param name="term">The semester's name.</param>
    /// <param name="academicYear">The academic year's name.</param>
    /// <returns>The two sentences.</returns>
    public static string TargetLine(string term, string academicYear) =>
        $"{TargetRule} The current semester is {Where(term, academicYear)}.";

    /// <summary>
    /// The target line when there is no semester to import into: the same rule, then what to do about it
    /// (<see cref="NoSemesterSelected"/> or <see cref="SeveralSemestersSelected"/>).
    /// </summary>
    /// <param name="selectedSemesterCount">How many semesters are selected; 0 or 1 gives the "no semester" sentence (1 only if its details couldn't be read), more gives the "several" one.</param>
    /// <returns>The two sentences.</returns>
    public static string NoTargetLine(int selectedSemesterCount) =>
        $"{TargetRule} {(selectedSemesterCount > 1 ? SeveralSemestersSelected : NoSemesterSelected)}";

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
    /// The problem for a cell the file reader could not use (see <see cref="CellIssue"/>): an Excel error cell
    /// ("This cell shows an Excel error (#N/A). Fix the formula in Excel, or type the value instead.") or a cell
    /// Excel turned into a date ("Excel has turned this into a date (2 Jan 2026). Format the column as Text in
    /// Excel and type the value again."). Each says what happened to the cell and what to do about it. An error
    /// the reader cannot name (no text for <see cref="CellIssue.Shown"/>) is worded without the brackets.
    /// </summary>
    /// <param name="issue">The issue the reader recorded for the cell.</param>
    /// <returns>The message, one or two sentences with no row or column (the problem carries those).</returns>
    public static string CellIssueProblem(CellIssue issue) => issue.Kind switch
    {
        CellIssueKind.Date =>
            $"Excel has turned this into a date ({issue.Shown}). Format the column as Text in Excel and type the value again.",
        _ => string.IsNullOrEmpty(issue.Shown)
            ? "This cell shows an Excel error. Fix the formula in Excel, or type the value instead."
            : $"This cell shows an Excel error ({issue.Shown}). Fix the formula in Excel, or type the value instead.",
    };

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
