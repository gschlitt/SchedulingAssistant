using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Display wrapper for a single meeting row in the meeting list panel.
/// Holds pre-formatted strings so the view needs no converter logic.
/// </summary>
public partial class MeetingListItemViewModel : ObservableObject
{
    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>The underlying meeting model.</summary>
    public Meeting Meeting { get; }

    /// <summary>The meeting title shown as the card heading.</summary>
    public string Title => Meeting.Title.Length > 0 ? Meeting.Title : "(untitled)";

    /// <summary>Formatted schedule lines, e.g. ["Mon  0900–1030  Rm 101"].</summary>
    public IReadOnlyList<string> ScheduleLines { get; }

    /// <summary>Comma-separated attendee names, or null when no attendees are assigned.</summary>
    public string? AttendeeLine { get; }

    /// <summary>Optional note preview text.</summary>
    public string? NoteLine { get; }

    /// <summary>True when the card's detail section is showing.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// True for a placeholder card created by the Add command before the meeting has
    /// been saved. When true the card summary (title, schedule, attendees) is hidden
    /// so the editor form fills the card without a blank heading above it.
    /// </summary>
    [ObservableProperty] private bool _isBeingCreated;

    public MeetingListItemViewModel(
        Meeting meeting,
        Dictionary<string, Instructor> instructorLookup,
        Dictionary<string, Room> roomLookup)
    {
        Meeting = meeting;

        ScheduleLines = meeting.Schedule
            .OrderBy(s => s.Day).ThenBy(s => s.StartMinutes)
            .Select(s =>
            {
                var day   = s.Day >= 1 && s.Day <= 6 ? DayNames[s.Day] : $"Day {s.Day}";
                var start = FormatMinutes(s.StartMinutes);
                var end   = FormatMinutes(s.EndMinutes);
                var room  = s.RoomId is not null && roomLookup.TryGetValue(s.RoomId, out var r)
                    ? $"  {r.Building} {r.RoomNumber}".TrimEnd()
                    : string.Empty;
                return $"{day}  {start}–{end}{room}";
            })
            .ToList();

        var attendeeNames = meeting.InstructorAssignments
            .Select(a => instructorLookup.TryGetValue(a.InstructorId, out var i)
                ? $"{i.FirstName} {i.LastName}" : null)
            .Where(n => n is not null)
            .ToList();
        AttendeeLine = attendeeNames.Count > 0 ? string.Join(", ", attendeeNames) : null;

        NoteLine = !string.IsNullOrWhiteSpace(meeting.Notes) ? meeting.Notes : null;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}
