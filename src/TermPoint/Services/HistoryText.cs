using System.Globalization;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services;

/// <summary>
/// One meeting moved by a drag: where it was and where it is now.
/// </summary>
/// <param name="FromDay">Day before (1 = Monday ... 6 = Saturday).</param>
/// <param name="FromStart">Start before, in minutes after midnight.</param>
/// <param name="ToDay">Day after.</param>
/// <param name="ToStart">Start after, in minutes after midnight.</param>
public sealed record MeetingMove(int FromDay, int FromStart, int ToDay, int ToStart)
{
    /// <summary>
    /// The meetings a drag moved: compares two index-aligned lists (<c>MeetingMover.Move</c> returns the meetings in the
    /// same order) and gives one move per meeting whose day or start changed.
    /// </summary>
    /// <param name="before">The meetings before the drag.</param>
    /// <param name="after">The meetings after it, same order.</param>
    /// <returns>The moves, ordered by (FromDay, FromStart); empty when nothing moved.</returns>
    public static IReadOnlyList<MeetingMove> Between(IReadOnlyList<SectionDaySchedule> before, IReadOnlyList<SectionDaySchedule> after)
    {
        var moves = new List<MeetingMove>();
        for (var i = 0; i < Math.Min(before.Count, after.Count); i++)
        {
            var (b, a) = (before[i], after[i]);
            if (b.Day != a.Day || b.StartMinutes != a.StartMinutes)
                moves.Add(new MeetingMove(b.Day, b.StartMinutes, a.Day, a.StartMinutes));
        }
        return moves.OrderBy(m => m.FromDay).ThenBy(m => m.FromStart).ToList();
    }
}

/// <summary>
/// Every user-facing string of the undo feature, in one place (<c>designs/1.2.5/undo.md</c>, "Wording"). The step
/// labels, the "Undone:" lines, the tooltips, the history list and the close question are approved by Greg
/// (2026-11-26); strings marked "new wording, not yet approved" are not, and need his OK before release.
/// </summary>
public static class HistoryText
{
    private const string Arrow = "→";   // →

    // ── Step labels ──────────────────────────────────────────────────────────

    /// <summary>
    /// The heading of a section, as the Section List shows it ("CHEM 101 B"; just the code when there is no course).
    /// </summary>
    /// <param name="courseCode">The course's calendar code, or null.</param>
    /// <param name="sectionCode">The section code.</param>
    /// <returns>The heading.</returns>
    public static string Heading(string? courseCode, string sectionCode)
        => SectionListItemViewModel.BuildHeading(courseCode, sectionCode);

    /// <summary>
    /// The heading of a logged section row (its readable course code and section code).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The heading.</returns>
    public static string Heading(SectionRow row) => Heading(row.CourseCode, row.SectionCode ?? "");

    /// <summary>
    /// The label of a drag. One meeting: "Move CHEM 101 B Thu 10:00 → Fri 13:00". When the day does not change only the
    /// time does: "Move CHEM 101 B Thu 10:00 → 11:30". Several meetings that all move from one start time to another
    /// show their days once: "Move CHEM 101 B Tue, Thu 10:00 → 11:30" (and, if their days changed too,
    /// "Tue, Thu 10:00 → Wed, Fri 13:00"). Meetings that start at different times each give their own part, joined by
    /// a comma. approved wording (Greg, 2026-11-26).
    /// </summary>
    /// <param name="heading">The section's heading.</param>
    /// <param name="moves">The meetings moved, in display order.</param>
    /// <returns>The label.</returns>
    public static string Move(string heading, IReadOnlyList<MeetingMove> moves)
    {
        if (moves.Count == 0) return $"Move {heading}";
        var sameTimes = moves.All(m => m.FromStart == moves[0].FromStart && m.ToStart == moves[0].ToStart);
        if (!sameTimes)
            return $"Move {heading} " + string.Join(", ", moves.Select(m => MovePart(new[] { m })));
        return $"Move {heading} {MovePart(moves)}";
    }

    /// <summary>
    /// The "days time → time" part of a move label for moves that share both start times.
    /// </summary>
    /// <param name="moves">At least one move, all from the same start to the same start.</param>
    /// <returns>For example "Thu 10:00 → Fri 13:00" or "Tue, Thu 10:00 → 11:30".</returns>
    private static string MovePart(IReadOnlyList<MeetingMove> moves)
    {
        var fromDays = JoinDays(moves.Select(m => m.FromDay));
        var from = FormatTime(moves[0].FromStart);
        var to = FormatTime(moves[0].ToStart);
        return moves.All(m => m.FromDay == m.ToDay)
            ? $"{fromDays} {from} {Arrow} {to}"
            : $"{fromDays} {from} {Arrow} {JoinDays(moves.Select(m => m.ToDay))} {to}";
    }

    /// <summary>"Change instructors of HIST 101 A". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The section's heading.</param>
    public static string ChangeInstructors(string heading) => $"Change instructors of {heading}";

    /// <summary>"Change room of HIST 101 A". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The section's heading.</param>
    public static string ChangeRoom(string heading) => $"Change room of {heading}";

    /// <summary>"Change tags of HIST 101 A". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The section's heading.</param>
    public static string ChangeTags(string heading) => $"Change tags of {heading}";

    /// <summary>"Change flag of HIST 101 A". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The section's heading.</param>
    public static string ChangeFlag(string heading) => $"Change flag of {heading}";

    /// <summary>"Edit HIST 101 A". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The section's heading.</param>
    public static string Edit(string heading) => $"Edit {heading}";

    /// <summary>"Add HIST 101 C". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The new section's heading.</param>
    public static string Add(string heading) => $"Add {heading}";

    /// <summary>"Copy HIST 101 A as HIST 101 D". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="sourceHeading">The copied section's heading.</param>
    /// <param name="newHeading">The copy's heading.</param>
    public static string Copy(string sourceHeading, string newHeading) => $"Copy {sourceHeading} as {newHeading}";

    /// <summary>"Delete HIST 101 A". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="heading">The deleted section's heading.</param>
    public static string Delete(string heading) => $"Delete {heading}";

    /// <summary>
    /// "Apply tags to 4 HIST 101 sections". approved wording (Greg, 2026-11-26) for the plural; the singular
    /// ("Apply tags to 1 HIST 101 section") is new wording, not yet approved.
    /// </summary>
    /// <param name="count">How many sections were changed.</param>
    /// <param name="courseCode">The course's calendar code.</param>
    public static string ApplyTags(int count, string courseCode)
        => count == 1 ? $"Apply tags to 1 {courseCode} section" : $"Apply tags to {count} {courseCode} sections";

    // ── After an undo or redo ────────────────────────────────────────────────

    /// <summary>
    /// The line shown after an undo: "Undone: Move ..."; with the semester when it is not shown
    /// ("Undone: Delete HIST 102 A (Winter 2026, not shown)"); then one sentence per thing that could not be put back
    /// ("Undone: Change tags of HIST 101 A. The tag Lab has been deleted, so it wasn't put back."). approved wording
    /// (Greg, 2026-11-26).
    /// </summary>
    /// <param name="label">The step's label.</param>
    /// <param name="notShownSemester">The semester's name when it is not among the shown ones; otherwise null.</param>
    /// <param name="sentences">Sentences about what was not put back (may be empty).</param>
    public static string Undone(string label, string? notShownSemester, IEnumerable<string> sentences)
        => Compose("Undone", label, notShownSemester, sentences);

    /// <summary>
    /// The line shown after a redo: "Redone: ...", built like <see cref="Undone"/>. new wording, not yet approved.
    /// </summary>
    /// <param name="label">The step's label.</param>
    /// <param name="notShownSemester">The semester's name when it is not among the shown ones; otherwise null.</param>
    /// <param name="sentences">Sentences about what was not put back (may be empty).</param>
    public static string Redone(string label, string? notShownSemester, IEnumerable<string> sentences)
        => Compose("Redone", label, notShownSemester, sentences);

    /// <summary>Builds "Verb: label (semester, not shown). sentence sentence".</summary>
    private static string Compose(string verb, string label, string? notShownSemester, IEnumerable<string> sentences)
    {
        var line = $"{verb}: {label}{NotShown(notShownSemester)}";
        var extra = sentences.ToList();
        return extra.Count == 0 ? line : $"{line}. {string.Join(" ", extra)}";
    }

    /// <summary>
    /// The suffix " (Winter 2026, not shown)" (empty for a null semester). approved wording (Greg, 2026-11-26).
    /// </summary>
    /// <param name="semesterName">The semester's name, or null.</param>
    public static string NotShown(string? semesterName)
        => string.IsNullOrEmpty(semesterName) ? "" : $" ({semesterName}, not shown)";

    /// <summary>
    /// The sentence naming what was deleted since and so was not put back. One thing: "The tag Lab has been deleted, so
    /// it wasn't put back." (approved wording, Greg, 2026-11-26.) Several: "The tag Lab and the room B204 have been
    /// deleted, so they weren't put back." (new wording, not yet approved; three or more read "The a, the b and the c ...".)
    /// </summary>
    /// <param name="things">Each thing as (kind, name); a null or empty name leaves just "the tag".</param>
    /// <returns>The sentence; an empty string when there are no things.</returns>
    public static string DeletedSince(IReadOnlyList<(string Kind, string? Name)> things)
    {
        if (things.Count == 0) return "";
        var parts = things.Select(t => string.IsNullOrWhiteSpace(t.Name) ? $"the {t.Kind}" : $"the {t.Kind} {t.Name}").ToList();
        var list = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        var first = char.ToUpperInvariant(list[0]) + list[1..];
        return parts.Count == 1
            ? $"{first} has been deleted, so it wasn't put back."
            : $"{first} have been deleted, so they weren't put back.";
    }

    /// <summary>
    /// "HIST 101 A wasn't put back: the course HIST 101 has been deleted." approved wording (Greg, 2026-11-26) for the
    /// course; the semester form ("...: the semester Winter 2026 has been deleted.") is new wording, not yet approved.
    /// </summary>
    /// <param name="sectionHeading">The section's heading.</param>
    /// <param name="kind">"course" or "semester".</param>
    /// <param name="name">The course's calendar code or the semester's name.</param>
    public static string SectionNotPutBack(string sectionHeading, string kind, string name)
        => $"{sectionHeading} wasn't put back: the {kind} {name} has been deleted.";

    /// <summary>
    /// "HIST 101 A wasn't put back: the semester Winter 2026 has been emptied." The section was removed by Empty Semester.
    /// new wording, not yet approved.
    /// </summary>
    /// <param name="sectionHeading">The section's heading.</param>
    /// <param name="semesterName">The emptied semester's name.</param>
    public static string SectionNotPutBackEmptied(string sectionHeading, string semesterName)
        => $"{sectionHeading} wasn't put back: the semester {semesterName} has been emptied.";

    /// <summary>
    /// The name of a deleted thing, read from its logged <c>data</c> JSON, shown the way the app shows it: a room as
    /// building and number (as the room pickers), an instructor as "First Last", a scheduling-environment value by its
    /// name.
    /// </summary>
    /// <param name="reference">What was removed (its table says how to read the data).</param>
    /// <param name="dataJson">The deleted row's data (<c>ISectionChangeLog.DeletedData</c>), or null.</param>
    /// <returns>The name, or null when it cannot be read.</returns>
    public static string? NameOf(RemovedReference reference, string? dataJson) => reference.Table switch
    {
        SectionReferences.RoomsTable => Read<Room>(dataJson, r => $"{r.Building} {r.RoomNumber}".Trim()),
        SectionReferences.InstructorsTable => Read<Instructor>(dataJson, i => $"{i.FirstName} {i.LastName}".Trim()),
        SectionReferences.EnvironmentValuesTable => Read<SchedulingEnvironmentValue>(dataJson, v => v.Name),
        _ => null,
    };

    /// <summary>The calendar code of a course read from its logged data, or null.</summary>
    /// <param name="dataJson">The deleted course row's data.</param>
    public static string? CourseCodeOf(string? dataJson) => Read<Course>(dataJson, c => c.CalendarCode);

    /// <summary>The name of a semester read from its logged data, or null.</summary>
    /// <param name="dataJson">The deleted semester row's data.</param>
    public static string? SemesterNameOf(string? dataJson) => Read<Semester>(dataJson, s => s.Name);

    /// <summary>Deserializes <paramref name="json"/> and reads one value; null for missing or unreadable data.</summary>
    private static string? Read<T>(string? json, Func<T, string> pick)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var value = pick(JsonHelpers.Deserialize<T>(json));
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The refusal when an undo finds a section changed another way (it should never show). approved wording (Greg,
    /// 2026-11-26).
    /// </summary>
    public static string HistoryCut =>
        "This change can't be undone because the section was changed another way. " + EarlierChangesCantBeUndoneOneByOne + ".";

    // ── Buttons, list ────────────────────────────────────────────────────────

    /// <summary>"Undo: Move ... (Ctrl+Z)". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="label">The next undoable step's label.</param>
    public static string UndoTooltip(string label) => $"Undo: {label} (Ctrl+Z)";

    /// <summary>The Undo tooltip when there is nothing to undo. approved wording (Greg, 2026-11-26).</summary>
    public const string NothingToUndo = "Nothing to undo since your last save";

    /// <summary>"Redo: ... (Ctrl+Y)". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="label">The next redoable step's label.</param>
    public static string RedoTooltip(string label) => $"Redo: {label} (Ctrl+Y)";

    /// <summary>The Redo tooltip when there is nothing to redo. new wording, not yet approved.</summary>
    public const string NothingToRedo = "Nothing to redo";

    /// <summary>Hover text of a history line: it and every newer line will be undone. approved wording (Greg, 2026-11-26).</summary>
    public const string TheseWillBeUndone = "These will be undone";

    /// <summary>The history list's last line: "Your last save · 10:42". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="at">When the last save was made (or the session started).</param>
    public static string LastSave(DateTime at) => $"Your last save · {FormatClock(at)}";

    /// <summary>
    /// The history list's last line after the history was cut. approved wording (Greg, 2026-11-26).
    /// </summary>
    public const string EarlierChangesCantBeUndoneOneByOne = "Earlier changes can't be undone one by one";

    /// <summary>A step's line in the list: "Move ... · 10:58". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="label">The step's label.</param>
    /// <param name="at">When the step was made.</param>
    public static string StepLine(string label, DateTime at) => $"{label} · {FormatClock(at)}";

    // ── Unsaved line, close question ─────────────────────────────────────────

    /// <summary>"Unsaved changes since 10:42". approved wording (Greg, 2026-11-26).</summary>
    /// <param name="since">When the changes began.</param>
    public static string UnsavedSince(DateTime since) => $"Unsaved changes since {FormatClock(since)}";

    /// <summary>
    /// The one-time notice after two hours unsaved. approved wording (Greg, 2026-11-26).
    /// </summary>
    /// <param name="since">When the changes began.</param>
    public static string TwoHourNotice(DateTime since)
        => $"You haven't saved since {FormatClock(since)}. Colleagues can't see your changes until you save.";

    /// <summary>The close question's title. approved wording (Greg, 2026-11-26).</summary>
    public const string CloseTitle = "Save Changes?";

    /// <summary>The close question's body. approved wording (Greg, 2026-11-26).</summary>
    /// <param name="lastSave">When the user last saved (or the session started).</param>
    public static string CloseBody(DateTime lastSave)
        => $"You've changed the schedule since you last saved at {FormatClock(lastSave)}. " +
           "Colleagues won't see these changes until you save.";

    /// <summary>The extra sentence when a card also has unapplied changes. approved wording (Greg, 2026-11-26).</summary>
    /// <param name="cardHeading">The open card's heading.</param>
    public static string CloseCardSentence(string cardHeading)
        => $"{cardHeading}'s card has changes you haven't applied; Save applies them too.";

    /// <summary>
    /// The close question's body when nothing has been saved since the database was opened, so there is no "last saved"
    /// time to name (the open may also have restored changes from before it). new wording, not yet approved.
    /// </summary>
    public const string CloseBodyNotSavedYet =
        "You've changed the schedule and haven't saved yet. Colleagues won't see these changes until you save.";

    /// <summary>
    /// The extra sentence when an editor other than an existing section's card has unapplied changes (a new section, an
    /// instructor, a course...). new wording, not yet approved.
    /// </summary>
    public const string CloseEditorSentence = "The open editor has changes you haven't applied; Save applies them too.";

    /// <summary>
    /// The close question's whole body when only a section card has unapplied changes and nothing else is unsaved.
    /// new wording, not yet approved.
    /// </summary>
    /// <param name="cardHeading">The open card's heading.</param>
    public static string CloseBodyCardOnly(string cardHeading)
        => $"{cardHeading}'s card has changes you haven't applied. Save applies and saves them.";

    /// <summary>
    /// The close question's whole body when only another editor has unapplied changes and nothing else is unsaved.
    /// new wording, not yet approved.
    /// </summary>
    public const string CloseBodyEditorOnly = "The open editor has changes you haven't applied. Save applies and saves them.";

    /// <summary>The question before opening another database. approved wording (Greg, 2026-11-26).</summary>
    /// <param name="currentName">The open database's name.</param>
    /// <param name="nextName">The database about to be opened.</param>
    public static string SwitchTitle(string currentName, string nextName)
        => $"Save your changes to {currentName} before opening {nextName}?";

    /// <summary>The question before restoring a backup. approved wording (Greg, 2026-11-26).</summary>
    /// <param name="currentName">The open database's name.</param>
    public static string RestoreTitle(string currentName)
        => $"Save your changes to {currentName} before restoring a backup?";

    /// <summary>The Save button of the close question. approved wording (Greg, 2026-11-26).</summary>
    public const string SaveButton = "Save";

    /// <summary>The Don't Save button of the close question. approved wording (Greg, 2026-11-26).</summary>
    public const string DontSaveButton = "Don't Save";

    /// <summary>The Cancel button of the close question. approved wording (Greg, 2026-11-26).</summary>
    public const string CancelButton = "Cancel";

    /// <summary>The "What's new" line. approved wording (Greg, 2026-11-26).</summary>
    public const string WhatsNew =
        "TermPoint no longer saves on its own. Your changes reach colleagues when you press Save, and until then you " +
        "can undo section changes with Ctrl+Z. When you close with unsaved changes, TermPoint asks whether to save them.";

    // ── Formatting ───────────────────────────────────────────────────────────

    /// <summary>A time of day as 24-hour "HH:mm".</summary>
    /// <param name="minutes">Minutes after midnight.</param>
    public static string FormatTime(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

    /// <summary>A clock time as 24-hour "HH:mm" (the format of every time in the feature's wording).</summary>
    /// <param name="at">The moment.</param>
    private static string FormatClock(DateTime at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Day names as <c>SectionListItemViewModel</c> shows them ("Mon".."Sat"; "Day N" otherwise), distinct, comma-joined.</summary>
    /// <param name="days">Day numbers, 1 = Monday.</param>
    private static string JoinDays(IEnumerable<int> days)
        => string.Join(", ", days.Distinct().Select(d =>
            d >= 1 && d < SectionListItemViewModel.DayNames.Length ? SectionListItemViewModel.DayNames[d] : $"Day {d}"));
}
