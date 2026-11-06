using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Finds the room and instructor conflicts a section would have if it were saved as it is now in the editor
/// (design: <c>designs/conflict-muting.md</c>, Part A). It runs the same detectors the section cards use
/// (<see cref="RoomConflictService"/> and <see cref="InstructorConflictService"/>), so the editor and the
/// cards always agree on what a conflict is and how it is worded.
/// </summary>
public static class DraftConflictCheck
{
    /// <summary>
    /// Returns the conflict descriptions for <paramref name="draft"/> against the other sections of its semester.
    /// The saved copy of the same section (matched by ID) is replaced by the draft, so a conflict the edit has
    /// already removed is not reported, and a section never conflicts with its own saved self.
    /// </summary>
    /// <param name="draft">
    /// The section as edited but not saved. Only its ID, schedule and instructor assignments are read.
    /// It is never stored.
    /// </param>
    /// <param name="semesterSections">All saved sections of the draft's semester (may include the draft's saved copy).</param>
    /// <param name="roomNameById">Room display name keyed by room ID (e.g. "A 121").</param>
    /// <param name="instructorLabelById">Instructor display label keyed by instructor ID (e.g. "J. Smith").</param>
    /// <param name="courseCodeById">Calendar code keyed by course ID (e.g. "CHEM101").</param>
    /// <returns>Room conflicts first, then instructor conflicts, one description each; empty when there are none.</returns>
    public static IReadOnlyList<string> Check(
        Section draft,
        IReadOnlyList<Section> semesterSections,
        Dictionary<string, string> roomNameById,
        Dictionary<string, string> instructorLabelById,
        Dictionary<string, string> courseCodeById)
    {
        // The semester as it would be after the save: every other section, plus the draft.
        var sections = new List<Section>(semesterSections.Count + 1);
        foreach (var s in semesterSections)
            if (s.Id != draft.Id) sections.Add(s);
        sections.Add(draft);

        var lines = new List<string>();
        if (RoomConflictService.DetectConflicts(sections, roomNameById, courseCodeById)
                .TryGetValue(draft.Id, out var roomLines))
            lines.AddRange(roomLines);
        if (InstructorConflictService.DetectConflicts(sections, instructorLabelById, courseCodeById)
                .TryGetValue(draft.Id, out var instructorLines))
            lines.AddRange(instructorLines);
        return lines;
    }
}
