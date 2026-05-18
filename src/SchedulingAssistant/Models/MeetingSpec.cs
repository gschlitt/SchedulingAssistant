namespace SchedulingAssistant.Models;

/// <summary>
/// A partially-specified meeting passed from the section editor to the Room Availability Browser.
/// The user fills in what they know; the browser solver fills in the gaps.
/// </summary>
/// <param name="Index">Ordinal position in the Meetings collection — used to map accepted solutions back to the correct meeting row.</param>
/// <param name="Day">Day of week (1–7), or null when the user hasn't chosen a day yet.</param>
/// <param name="DurationMinutes">Meeting duration in minutes. Always required for Browse.</param>
/// <param name="StartMinutes">Start time in minutes from midnight, or null when unspecified.</param>
/// <param name="RoomTypeId">Room type filter for this specific meeting, or null for any room type.</param>
/// <param name="RoomId">Locked room — solver constrains to this room only. Null when unspecified.</param>
/// <param name="IsRemote">True for remote/online meetings — excluded from the solver entirely.</param>
public record MeetingSpec(
    int Index,
    int? Day,
    int DurationMinutes,
    int? StartMinutes,
    string? RoomTypeId,
    string? RoomId = null,
    bool IsRemote = false,
    string? Frequency = null);

/// <summary>
/// A fully-resolved solution for one meeting spec, ready to be written back to the section editor.
/// </summary>
/// <param name="SpecIndex">Maps back to <see cref="MeetingSpec.Index"/> / Meetings[i].</param>
/// <param name="Day">Assigned day of week (1–7).</param>
/// <param name="StartMinutes">Assigned start time in minutes from midnight.</param>
/// <param name="DurationMinutes">Meeting duration in minutes (echoed from the spec).</param>
/// <param name="RoomId">Assigned room ID.</param>
/// <param name="RoomLabel">Display label for the room, e.g. "A 101".</param>
public record SpecSolution(
    int SpecIndex,
    int Day,
    int StartMinutes,
    int DurationMinutes,
    string RoomId,
    string RoomLabel);
