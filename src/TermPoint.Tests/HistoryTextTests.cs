using TermPoint.Data;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="HistoryText"/>: the approved step labels and sentences of the undo feature
/// (<c>designs/1.2.5/undo.md</c>, "Wording").
/// </summary>
public sealed class HistoryTextTests
{
    private const int Mon = 1, Tue = 2, Wed = 3, Thu = 4, Fri = 5;

    /// <summary>One meeting moved to another day and time: "Thu 10:00 → Fri 13:00".</summary>
    [Fact]
    public void Move_OneMeeting_DayAndTime()
    {
        Assert.Equal("Move CHEM 101 B Thu 10:00 → Fri 13:00",
            HistoryText.Move("CHEM 101 B", new[] { new MeetingMove(Thu, 600, Fri, 780) }));
        Assert.Equal("Move CHEM 111 L02 Tue 14:30 → Wed 08:30",
            HistoryText.Move("CHEM 111 L02", new[] { new MeetingMove(Tue, 870, Wed, 510) }));
    }

    /// <summary>When only the time changes, the day is shown once: "Thu 10:00 → 11:30".</summary>
    [Fact]
    public void Move_OneMeeting_OnlyTheTimeChanges()
        => Assert.Equal("Move CHEM 101 B Thu 10:00 → 11:30",
            HistoryText.Move("CHEM 101 B", new[] { new MeetingMove(Thu, 600, Thu, 690) }));

    /// <summary>Two meetings with the same time change show their days together: "Tue, Thu 10:00 → 11:30".</summary>
    [Fact]
    public void Move_TwoMeetings_OnlyTheTimeChanges()
    {
        Assert.Equal("Move CHEM 101 B Tue, Thu 10:00 → 11:30",
            HistoryText.Move("CHEM 101 B", new[] { new MeetingMove(Tue, 600, Tue, 690), new MeetingMove(Thu, 600, Thu, 690) }));
        Assert.Equal("Move CHEM 101 B Tue, Thu 10:00 → Wed, Fri 13:00",
            HistoryText.Move("CHEM 101 B", new[] { new MeetingMove(Tue, 600, Wed, 780), new MeetingMove(Thu, 600, Fri, 780) }));
    }

    /// <summary>Meetings that start at different times each give their own part.</summary>
    [Fact]
    public void Move_DifferentStarts_EachGivesItsOwnPart()
        => Assert.Equal("Move CHEM 101 B Mon 08:00 → 09:00, Wed 10:00 → Thu 10:00",
            HistoryText.Move("CHEM 101 B", new[] { new MeetingMove(Mon, 480, Mon, 540), new MeetingMove(Wed, 600, Thu, 600) }));

    /// <summary>Times are 24-hour with a leading zero; an out-of-range day reads "Day N".</summary>
    [Fact]
    public void Move_FormatsTimesAndOddDays()
        => Assert.Equal("Move X Day 7 07:05 → Mon 00:00",
            HistoryText.Move("X", new[] { new MeetingMove(7, 425, Mon, 0) }));

    /// <summary>The other step labels are exactly the approved ones.</summary>
    [Fact]
    public void Labels_AreTheApprovedWording()
    {
        Assert.Equal("Change instructors of HIST 101 A", HistoryText.ChangeInstructors("HIST 101 A"));
        Assert.Equal("Change room of HIST 101 A", HistoryText.ChangeRoom("HIST 101 A"));
        Assert.Equal("Change tags of HIST 101 A", HistoryText.ChangeTags("HIST 101 A"));
        Assert.Equal("Change flag of HIST 101 A", HistoryText.ChangeFlag("HIST 101 A"));
        Assert.Equal("Edit HIST 101 A", HistoryText.Edit("HIST 101 A"));
        Assert.Equal("Add HIST 101 C", HistoryText.Add("HIST 101 C"));
        Assert.Equal("Copy HIST 101 A as HIST 101 D", HistoryText.Copy("HIST 101 A", "HIST 101 D"));
        Assert.Equal("Delete HIST 101 A", HistoryText.Delete("HIST 101 A"));
        Assert.Equal("Apply tags to 4 HIST 101 sections", HistoryText.ApplyTags(4, "HIST 101"));
        Assert.Equal("HIST 101 A", HistoryText.Heading("HIST 101", "A"));
        Assert.Equal("A", HistoryText.Heading(null, "A"));
    }

    /// <summary>The "Undone:" line, with the not-shown note and the sentences about what was not put back.</summary>
    [Fact]
    public void Undone_Lines()
    {
        Assert.Equal("Undone: Move CHEM 111 L02 Tue 14:30 → Wed 08:30",
            HistoryText.Undone("Move CHEM 111 L02 Tue 14:30 → Wed 08:30", null, []));
        Assert.Equal("Undone: Delete HIST 102 A (Winter 2026, not shown)",
            HistoryText.Undone("Delete HIST 102 A", "Winter 2026", []));
        Assert.Equal("Undone: Change tags of HIST 101 A. The tag Lab has been deleted, so it wasn't put back.",
            HistoryText.Undone("Change tags of HIST 101 A", null,
                [HistoryText.DeletedSince([("tag", "Lab")])]));
        Assert.Equal("Redone: Edit HIST 101 A", HistoryText.Redone("Edit HIST 101 A", null, []));
    }

    /// <summary>One deleted thing: the approved sentence. Several: the plural form. Two or three are joined with "and".</summary>
    [Fact]
    public void DeletedSince_SingularAndPlural()
    {
        Assert.Equal("The tag Lab has been deleted, so it wasn't put back.",
            HistoryText.DeletedSince([("tag", "Lab")]));
        Assert.Equal("The tag Lab and the room B204 have been deleted, so they weren't put back.",
            HistoryText.DeletedSince([("tag", "Lab"), ("room", "B204")]));
        Assert.Equal("The tag Lab, the room B204 and the instructor Ann Lee have been deleted, so they weren't put back.",
            HistoryText.DeletedSince([("tag", "Lab"), ("room", "B204"), ("instructor", "Ann Lee")]));
        Assert.Equal("The tag has been deleted, so it wasn't put back.", HistoryText.DeletedSince([("tag", null)]));
        Assert.Equal("", HistoryText.DeletedSince([]));
    }

    /// <summary>The section-stays-deleted sentences name the course or the semester.</summary>
    [Fact]
    public void SectionNotPutBack_NamesTheCourseOrSemester()
    {
        Assert.Equal("HIST 101 A wasn't put back: the course HIST 101 has been deleted.",
            HistoryText.SectionNotPutBack("HIST 101 A", "course", "HIST 101"));
        Assert.Equal("HIST 101 A wasn't put back: the semester Winter 2026 has been deleted.",
            HistoryText.SectionNotPutBack("HIST 101 A", "semester", "Winter 2026"));
    }

    /// <summary>Names come from the logged JSON the way the app shows them: room as building and number, instructor as first and last name.</summary>
    [Fact]
    public void NameOf_ReadsTheLoggedData()
    {
        Assert.Equal("B 204", HistoryText.NameOf(new RemovedReference("Rooms", "r", "room"),
            "{\"id\":\"r\",\"building\":\"B\",\"roomNumber\":\"204\"}"));
        Assert.Equal("Ann Lee", HistoryText.NameOf(new RemovedReference("Instructors", "i", "instructor"),
            "{\"id\":\"i\",\"firstName\":\"Ann\",\"lastName\":\"Lee\"}"));
        Assert.Equal("Lab", HistoryText.NameOf(new RemovedReference("SchedulingEnvironmentValues", "t", "tag"),
            "{\"id\":\"t\",\"name\":\"Lab\"}"));
        Assert.Null(HistoryText.NameOf(new RemovedReference("Rooms", "r", "room"), null));
        Assert.Null(HistoryText.NameOf(new RemovedReference("Rooms", "r", "room"), "not json"));
        Assert.Equal("HIST 101", HistoryText.CourseCodeOf("{\"calendarCode\":\"HIST 101\"}"));
        Assert.Equal("Winter 2026", HistoryText.SemesterNameOf("{\"name\":\"Winter 2026\"}"));
    }

    /// <summary>Tooltips, list lines, the cut message and the close question are the approved text.</summary>
    [Fact]
    public void ButtonsListAndCloseQuestion_AreTheApprovedWording()
    {
        var at = new DateTime(2026, 11, 26, 10, 42, 0);
        Assert.Equal("Undo: Edit HIST 101 A (Ctrl+Z)", HistoryText.UndoTooltip("Edit HIST 101 A"));
        Assert.Equal("Nothing to undo since your last save", HistoryText.NothingToUndo);
        Assert.Equal("Redo: Edit HIST 101 A (Ctrl+Y)", HistoryText.RedoTooltip("Edit HIST 101 A"));
        Assert.Equal("These will be undone", HistoryText.TheseWillBeUndone);
        Assert.Equal("Your last save · 10:42", HistoryText.LastSave(at));
        Assert.Equal("Earlier changes can't be undone one by one", HistoryText.EarlierChangesCantBeUndoneOneByOne);
        Assert.Equal("Move X Thu 10:00 → Fri 13:00 · 10:58",
            HistoryText.StepLine("Move X Thu 10:00 → Fri 13:00", new DateTime(2026, 11, 26, 10, 58, 0)));
        Assert.Equal("This change can't be undone because the section was changed another way. " +
                     "Earlier changes can't be undone one by one.", HistoryText.HistoryCut);

        Assert.Equal("Unsaved changes since 10:42", HistoryText.UnsavedSince(at));
        Assert.Equal("You haven't saved since 10:42. Colleagues can't see your changes until you save.",
            HistoryText.TwoHourNotice(at));
        Assert.Equal("Save Changes?", HistoryText.CloseTitle);
        Assert.Equal("You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until you save.",
            HistoryText.CloseBody(at));
        Assert.Equal("HIST 101 A's card has changes you haven't applied; Save applies them too.",
            HistoryText.CloseCardSentence("HIST 101 A"));
        Assert.Equal("Save your changes to BIOL before opening CHEM?", HistoryText.SwitchTitle("BIOL", "CHEM"));
        Assert.Equal("Save your changes to BIOL before restoring a backup?", HistoryText.RestoreTitle("BIOL"));
        Assert.Equal(("Save", "Don't Save", "Cancel"),
            (HistoryText.SaveButton, HistoryText.DontSaveButton, HistoryText.CancelButton));
    }
}
