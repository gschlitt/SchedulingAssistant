namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Marker interface for items that can appear in the Meeting List panel.
/// Implemented by both event cards (<see cref="MeetingListItemViewModel"/>) and
/// semester group banners (<see cref="SemesterBannerViewModel"/>).
/// Using a typed interface rather than <c>object</c> keeps the list collection
/// self-documenting and prevents arbitrary types from being inserted.
/// </summary>
public interface IMeetingListEntry { }
