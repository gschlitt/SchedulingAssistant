namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Marker interface for items that can appear in the Section List panel.
/// Implemented by both section cards (<see cref=SectionListItemViewModel/>) and
/// semester group banners (<see cref=SemesterBannerViewModel/>).
/// Using a typed interface rather than <c>object</c> keeps the list collection
/// self-documenting and prevents arbitrary types from being inserted.
/// </summary>
public interface ISectionListEntry { }
