using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Checks sections against an academic year's legal start-time matrix.
/// Reusable across copy-semester, start-time editing, etc.
/// </summary>
public class ScheduleValidationService(LegalStartTimeRepository legalStartTimeRepo)
{
    /// <summary>
    /// Returns sections whose schedule entries don't fit the given academic year's
    /// start-time matrix. A section is flagged if ANY meeting has a block length
    /// not in the matrix, or a start time not valid for that block length.
    /// Sections with no schedule entries are never flagged.
    /// </summary>
    public List<(Section section, List<SectionDaySchedule> badMeetings)> FindIncompatibleSections(
        List<Section> sections, string academicYearId)
    {
        var legalTimes = legalStartTimeRepo.GetAll(academicYearId);
        var matrix = legalTimes.ToDictionary(
            lt => lt.BlockLength,
            lt => new HashSet<int>(lt.StartTimes));

        var flagged = new List<(Section, List<SectionDaySchedule>)>();

        foreach (var section in sections)
        {
            if (section.Schedule.Count == 0) continue;

            var badMeetings = new List<SectionDaySchedule>();
            foreach (var meeting in section.Schedule)
            {
                var blockLength = meeting.DurationMinutes / 60.0;
                if (!matrix.TryGetValue(blockLength, out var validStarts)
                    || !validStarts.Contains(meeting.StartMinutes))
                {
                    badMeetings.Add(meeting);
                }
            }

            if (badMeetings.Count > 0)
                flagged.Add((section, badMeetings));
        }

        return flagged;
    }
}
