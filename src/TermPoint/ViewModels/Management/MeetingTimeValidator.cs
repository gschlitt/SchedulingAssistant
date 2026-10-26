using System.Collections.Generic;
using System.Linq;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Save-time completeness checks for schedule rows (<see cref="SectionMeetingViewModel"/>),
/// shared by the section editor and the meeting editor so both reject the same incomplete
/// rows with the same wording.
/// </summary>
/// <remarks>
/// A row with neither a start time nor a block length is an untouched row: it passes these
/// checks and is left out of the saved schedule by <see cref="SectionMeetingViewModel.ToSchedule"/>.
/// </remarks>
public static class MeetingTimeValidator
{
    /// <summary>
    /// Checks every row and returns the message for the first problem found, or null when all
    /// rows can be saved. Incomplete rows (a start time or a block length, but not both) are
    /// reported before rows with no day chosen.
    /// </summary>
    /// <param name="rows">The editor's schedule rows.</param>
    /// <param name="singular">What the editor calls one row, e.g. "meeting" or "meeting time".</param>
    /// <param name="plural">What the editor calls several rows, e.g. "meetings" or "meeting times".</param>
    /// <returns>A plain-language message telling the user what to fix, or null if there is nothing to fix.</returns>
    public static string? Validate(IEnumerable<SectionMeetingViewModel> rows, string singular, string plural)
    {
        var list = rows.ToList();

        var incomplete = list
            .Where(m => m.SelectedStartTime.HasValue != m.SelectedBlockLength.HasValue)
            .ToList();
        if (incomplete.Count > 1)
            return $"{incomplete.Count} {plural} are incomplete. Each needs both a start time and block length.";
        if (incomplete.Count == 1)
            return incomplete[0].SelectedStartTime.HasValue
                ? $"One {singular} has a start time but no block length. Please complete it or delete it."
                : $"One {singular} has a block length but no start time. Please complete it or delete it.";

        // An unset day (sentinel 0) is a placeholder for the Room Availability Browser to fill in;
        // a row that would be saved must have a real day.
        var unassignedDay = list
            .Count(m => m.SelectedDay == 0 && (m.SelectedStartTime.HasValue || m.SelectedBlockLength.HasValue));
        if (unassignedDay > 1)
            return $"{unassignedDay} {plural} have no day assigned. Use Browse to assign days, or select days manually.";
        if (unassignedDay == 1)
            return $"One {singular} has no day assigned. Use Browse to assign days, or select a day manually.";

        return null;
    }
}
