using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Discriminates the kind of bulk-selection preset a sentinel represents.
/// </summary>
public enum AttendeeSentinelKind
{
    /// <summary>Select all instructors.</summary>
    Everyone,

    /// <summary>Deselect all instructors.</summary>
    NoOne,

    /// <summary>Select only instructors whose <c>StaffTypeId</c> matches <see cref="AttendeeSentinelViewModel.StaffTypeId"/>.</summary>
    StaffType,
}

/// <summary>
/// A one-shot preset action shown at the top of the attendee popup.
/// Checking the sentinel applies the preset to <c>AttendeeSelections</c>
/// and then immediately resets itself so it does not appear persistently selected.
/// </summary>
public partial class AttendeeSentinelViewModel : ViewModelBase
{
    /// <summary>The label shown in the popup checkbox (e.g. "Everyone", "No one", "Sessional").</summary>
    public string DisplayName { get; }

    /// <summary>What kind of bulk-selection this sentinel performs.</summary>
    public AttendeeSentinelKind Kind { get; }

    /// <summary>
    /// For <see cref="AttendeeSentinelKind.StaffType"/> sentinels, the ID of the staff type
    /// whose instructors should be selected. Null for <c>Everyone</c> and <c>NoOne</c>.
    /// </summary>
    public string? StaffTypeId { get; }

    /// <summary>
    /// Bound to the popup checkbox. The <c>MeetingEditViewModel</c> watches this property
    /// and triggers the preset when it becomes true, then resets it to false.
    /// </summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Initialises the sentinel.
    /// </summary>
    /// <param name="displayName">Label shown in the popup.</param>
    /// <param name="kind">The bulk-selection action to perform.</param>
    /// <param name="staffTypeId">Required when <paramref name="kind"/> is <see cref="AttendeeSentinelKind.StaffType"/>; null otherwise.</param>
    public AttendeeSentinelViewModel(string displayName, AttendeeSentinelKind kind, string? staffTypeId = null)
    {
        DisplayName = displayName;
        Kind        = kind;
        StaffTypeId = staffTypeId;
    }
}
