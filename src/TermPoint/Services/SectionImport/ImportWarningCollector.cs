using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Builds the grouped warnings of an import (design Phase 4, "Warnings are grouped"): one
/// <see cref="ImportWarning"/> per problem, never one per row. Warnings are about the sections that
/// will be <b>added</b> only (a skipped section's cells are never looked at). The legal-start-time and
/// near-miss warnings live in <see cref="StartTimeWarnings"/> and <see cref="NearMissWarnings"/>; this
/// class adds the rest and orders them all: file-wide warnings first, then by the first row each affects.
/// </summary>
internal sealed class ImportWarningCollector
{
    private readonly ImportSetupSnapshot _snapshot;
    private readonly ImportPlan _plan;
    private readonly List<ImportWarning> _warnings = new();

    /// <summary>Creates a collector over a snapshot and the plan to warn about.</summary>
    /// <param name="snapshot">The database state the plan was made against.</param>
    /// <param name="plan">The plan, with its new sections (whose meetings are already computed).</param>
    public ImportWarningCollector(ImportSetupSnapshot snapshot, ImportPlan plan)
    {
        _snapshot = snapshot;
        _plan = plan;
    }

    /// <summary>Finds every warning.</summary>
    /// <returns>
    /// The warnings: file-wide ones first, then ordered by the first row they affect (warnings that tie
    /// keep the order they were found in).
    /// </returns>
    public IReadOnlyList<ImportWarning> Collect()
    {
        _warnings.AddRange(new StartTimeWarnings(_snapshot).Collect(_plan.NewSections));
        CollectCourseWarnings();
        CollectInstructorWarnings();
        _warnings.AddRange(new NearMissWarnings(_snapshot, _plan).Collect());
        CollectRoomCampusWarnings();
        CollectOverlapWarnings();

        // OrderBy is stable, so equal first rows keep the order in which the warnings were found.
        return _warnings
            .OrderBy(w => w.Rows.Count == 0 ? 0 : 1)
            .ThenBy(w => w.Rows.Count == 0 ? 0 : w.Rows[0])
            .ToList();
    }

    // ── Existing courses ──────────────────────────────────────────────────────

    /// <summary>
    /// For each existing course named by a new section: warns when the file's title differs from the
    /// stored one (the stored title is kept), and when the course is inactive. Each is one warning per
    /// course, naming the rows.
    /// </summary>
    private void CollectCourseWarnings()
    {
        var titleRows = new RowGrouping<CourseRef>(ReferenceEqualityComparer.Instance);
        var firstDifferentTitle = new Dictionary<CourseRef, string>(ReferenceEqualityComparer.Instance);
        var inactiveRows = new RowGrouping<CourseRef>(ReferenceEqualityComparer.Instance);

        foreach (var section in _plan.NewSections.Where(s => !s.Course.IsNew))
        {
            foreach (var (row, title) in section.CellsByRow(SectionImportColumns.CourseTitle))
            {
                if (title.Length == 0 || NameMatch.Equal(title, section.Course.Record.Title))
                    continue;
                firstDifferentTitle.TryAdd(section.Course, title);
                titleRows.Add(section.Course, row);
            }

            if (!section.Course.Record.IsActive)
                inactiveRows.Add(section.Course, section.RowNumbers[0]);
        }

        foreach (var group in titleRows.Entries)
            _warnings.Add(new ImportWarning(ImportWarningKinds.CourseTitleDiffers, group.Rows,
                $"{group.Key.Record.CalendarCode} is already called {group.Key.Record.Title} in TermPoint, " +
                $"so the file's title, {firstDifferentTitle[group.Key]}, isn't used ({ImportRowList.Format(group.Rows)})."));

        foreach (var group in inactiveRows.Entries)
            _warnings.Add(new ImportWarning(ImportWarningKinds.Inactive, group.Rows,
                $"{group.Key.Record.CalendarCode} is marked inactive in TermPoint ({ImportRowList.Format(group.Rows)}). " +
                "Its sections will still be added; reactivate it in Courses if it's offered again."));
    }

    // ── Instructors ───────────────────────────────────────────────────────────

    /// <summary>
    /// Warns about an existing instructor who is inactive, and about a new instructor who shares a last
    /// name with an existing one. One warning per instructor, naming the rows where the Instructor(s)
    /// cell names them.
    /// </summary>
    private void CollectInstructorWarnings()
    {
        var inactiveRows = new RowGrouping<InstructorRef>(ReferenceEqualityComparer.Instance);
        var newRows = new RowGrouping<InstructorRef>(ReferenceEqualityComparer.Instance);

        foreach (var section in _plan.NewSections)
        {
            var row = ImportRowText.FirstNonBlank(section.Rows, SectionImportColumns.Instructors)?.Row.RowNumber
                      ?? section.RowNumbers[0];
            foreach (var instructor in section.Instructors)
            {
                if (instructor.IsNew)
                    newRows.Add(instructor, row);
                else if (!instructor.Record.IsActive)
                    inactiveRows.Add(instructor, row);
            }
        }

        foreach (var group in inactiveRows.Entries)
            _warnings.Add(new ImportWarning(ImportWarningKinds.Inactive, group.Rows,
                $"{PersonName(group.Key.Record)} is marked inactive in TermPoint ({ImportRowList.Format(group.Rows)}). " +
                "Their sections will still be added; reactivate them in Instructors if they're teaching again."));

        foreach (var group in newRows.Entries)
        {
            var sameLastName = _snapshot.Instructors
                .Where(i => NameMatch.Equal(i.LastName, group.Key.Record.LastName))
                .Select(PersonName)
                .ToList();
            if (sameLastName.Count > 0)
                _warnings.Add(new ImportWarning(ImportWarningKinds.InstructorLastName, group.Rows,
                    $"Will add {PersonName(group.Key.Record)}. " +
                    $"{ImportText.AlreadyExist(sameLastName)} ({ImportRowList.Format(group.Rows)})."));
        }
    }

    /// <summary>An instructor's name as a message writes it: first name then last name.</summary>
    /// <param name="instructor">The instructor.</param>
    /// <returns>For example <c>John Smith</c>, or just the last name when there is no first name.</returns>
    private static string PersonName(Instructor instructor) =>
        $"{instructor.FirstName} {instructor.LastName}".Trim();

    // ── Rooms ─────────────────────────────────────────────────────────────────

    /// <summary>The identity of a room-campus warning: the room, its campus, and the section's campus.</summary>
    private readonly record struct RoomCampusKey(string RoomId, string RoomCampusId, string SectionCampusId);

    /// <summary>
    /// Warns when a meeting's room is on one campus but its section is on another (both set and
    /// different). One warning per (room, room campus, section campus), naming the meeting rows.
    /// </summary>
    private void CollectRoomCampusWarnings()
    {
        var rows = new RowGrouping<RoomCampusKey>();
        var rooms = new Dictionary<string, Room>();

        foreach (var section in _plan.NewSections.Where(s => s.Campus is not null))
        {
            var sectionCampusId = section.Campus!.Record.Id;
            foreach (var meetingRow in section.MeetingRows.Where(m => m.Room is not null))
            {
                var room = meetingRow.Room!.Record;
                if (string.IsNullOrEmpty(room.CampusId) || room.CampusId == sectionCampusId)
                    continue;
                rooms[room.Id] = room;
                rows.Add(new RoomCampusKey(room.Id, room.CampusId, sectionCampusId), meetingRow.RowNumber);
            }
        }

        var campusNames = _snapshot.Campuses.Concat(_plan.NewCampuses.Select(c => c.Record))
            .ToDictionary(c => c.Id, c => c.Name);

        foreach (var group in rows.Entries)
        {
            // A room pointing at a campus that no longer exists can't be named, so there is nothing to say.
            if (!campusNames.TryGetValue(group.Key.RoomCampusId, out var roomCampus)
                || !campusNames.TryGetValue(group.Key.SectionCampusId, out var sectionCampus))
                continue;

            var room = rooms[group.Key.RoomId];
            _warnings.Add(new ImportWarning(ImportWarningKinds.RoomCampus, group.Rows,
                $"Room {room.Building} {room.RoomNumber} is on the {roomCampus} campus, " +
                $"but its sections here are on {sectionCampus} ({ImportRowList.Format(group.Rows)})."));
        }
    }

    // ── Overlapping meetings ──────────────────────────────────────────────────

    /// <summary>
    /// Warns, once per section, when two of its meetings overlap on the same day in a week they both
    /// meet (design D11), naming the days and the rows of the meetings involved.
    /// </summary>
    private void CollectOverlapWarnings()
    {
        foreach (var section in _plan.NewSections)
        {
            var pairs = MeetingOverlap.ConflictingPairs(section.Meetings);
            if (pairs.Count == 0)
                continue;

            var days = pairs.Select(p => p.First.Day).Distinct().OrderBy(d => d).Select(ImportText.DayName).ToList();
            var rows = pairs.SelectMany(p => new[] { p.First.RowNumber, p.Second.RowNumber })
                .Distinct().OrderBy(r => r).ToList();
            _warnings.Add(new ImportWarning(ImportWarningKinds.OverlappingMeetings, rows,
                $"{section.Key.Label} has meetings at overlapping times on {ImportText.JoinWithAnd(days)} " +
                $"({ImportRowList.Format(rows)})."));
        }
    }
}
