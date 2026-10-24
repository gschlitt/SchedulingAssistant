using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.GridView;

public enum TileSubPanel { None, Instructors, Room, Tags, Flag }

public partial class SectionContextMenuViewModel : ObservableObject
{
    private readonly ISectionRepository _sectionRepo;
    private readonly Action _onSaved;
    private readonly WriteLockService _lockService;

    /// <summary>CanExecute predicate for the Confirm command; refuses writes in reader mode.</summary>
    private bool CanConfirm() => _lockService.IsWriter;

    private Section? _section;
    private int _meetingDay;
    private int _meetingStart;

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubPanel))]
    [NotifyPropertyChangedFor(nameof(IsInstructorPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsRoomPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsTagPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsFlagPanelVisible))]
    private TileSubPanel _activeSubPanel;

    public bool HasSubPanel          => ActiveSubPanel != TileSubPanel.None;
    public bool IsInstructorPanelVisible => ActiveSubPanel == TileSubPanel.Instructors;
    public bool IsRoomPanelVisible   => ActiveSubPanel == TileSubPanel.Room;
    public bool IsTagPanelVisible    => ActiveSubPanel == TileSubPanel.Tags;
    public bool IsFlagPanelVisible   => ActiveSubPanel == TileSubPanel.Flag;

    public ObservableCollection<ContextMenuItemVm> Instructors { get; } = [];
    public ObservableCollection<ContextMenuItemVm> Rooms       { get; } = [];
    public ObservableCollection<ContextMenuItemVm> Tags        { get; } = [];

    [ObservableProperty] private ContextMenuItemVm? _selectedRoom;

    /// <summary>The fixed list of flag choices (None + the three colors) shown in the Flag sub-panel.</summary>
    public ObservableCollection<FlagOptionVm> Flags { get; } = new(FlagOptionVm.CreateOptions());

    /// <summary>The flag option currently selected in the Flag sub-panel; committed on Confirm.</summary>
    [ObservableProperty] private FlagOptionVm? _selectedFlag;

    public SectionContextMenuViewModel(ISectionRepository sectionRepo, Action onSaved, WriteLockService lockService)
    {
        _sectionRepo = sectionRepo;
        _onSaved = onSaved;
        _lockService = lockService;
    }

    /// <summary>
    /// Fills the menu for the right-clicked tile: the section's instructors, the meeting's room, tags and flag.
    /// </summary>
    /// <param name="section">The section the tile belongs to.</param>
    /// <param name="meetingDay">Day of the right-clicked meeting (identifies which meeting's room to edit).</param>
    /// <param name="meetingStart">Start minutes of the right-clicked meeting.</param>
    /// <param name="instructors">Every instructor, already in display order (in-focus first under chosen subjects).</param>
    /// <param name="rooms">Every room.</param>
    /// <param name="tags">Every tag.</param>
    /// <param name="outOfFocusInstructorIds">
    /// Ids of instructors outside the chosen subjects (shown dimmed under "Other instructors"); null or empty means
    /// no grouping (All or None).
    /// </param>
    public void Load(
        Section section,
        int meetingDay,
        int meetingStart,
        IEnumerable<Instructor> instructors,
        IEnumerable<Room> rooms,
        IEnumerable<SchedulingEnvironmentValue> tags,
        IReadOnlySet<string>? outOfFocusInstructorIds = null)
    {
        _section      = section;
        _meetingDay   = meetingDay;
        _meetingStart = meetingStart;
        ActiveSubPanel = TileSubPanel.None;

        // Instructors — multi-select: active instructors, plus any inactive instructor the section already has.
        // Confirm rebuilds the assignments from the ticked rows, so an assigned instructor missing from the list
        // would be silently unassigned (the item 3 family of bug); listing them, ticked, keeps them.
        // Under chosen subjects (spec item 20) the caller has ordered the list in-focus first; out-of-focus rows are
        // dimmed, with an "Other instructors" heading above the first of them when an in-focus row precedes it.
        Instructors.Clear();
        var assignedIds = section.InstructorIds.ToHashSet();
        var anyInFocusYet = false;
        var headingShown = false;
        foreach (var inst in instructors.Where(i => i.IsActive || assignedIds.Contains(i.Id)))
        {
            var outOfFocus = outOfFocusInstructorIds?.Contains(inst.Id) == true;
            var heading = outOfFocus && anyInFocusYet && !headingShown;
            headingShown |= heading;
            anyInFocusYet |= !outOfFocus;
            Instructors.Add(new ContextMenuItemVm(
                inst.Id,
                $"{inst.FirstName} {inst.LastName}",
                assignedIds.Contains(inst.Id),
                outOfFocus,
                heading));
        }

        // Rooms — single-select; None at top
        Rooms.Clear();
        var meeting = section.Schedule
            .FirstOrDefault(s => s.Day == meetingDay && s.StartMinutes == meetingStart);
        var currentRoomId = meeting?.RoomId;

        var noneItem = new ContextMenuItemVm("", "(None)");
        Rooms.Add(noneItem);
        foreach (var room in rooms)
        {
            var label = string.IsNullOrWhiteSpace(room.Building)
                ? room.RoomNumber
                : $"{room.Building} {room.RoomNumber}";
            Rooms.Add(new ContextMenuItemVm(room.Id, label));
        }
        SelectedRoom = string.IsNullOrEmpty(currentRoomId)
            ? noneItem
            : Rooms.FirstOrDefault(r => r.Id == currentRoomId) ?? noneItem;

        // Tags — multi-select
        Tags.Clear();
        var assignedTagIds = section.TagIds.ToHashSet();
        foreach (var tag in tags)
            Tags.Add(new ContextMenuItemVm(tag.Id, tag.Name, assignedTagIds.Contains(tag.Id)));

        // Flag — single-select; seed from the section's current flag.
        SelectedFlag = Flags.FirstOrDefault(f => f.Value == section.Flag) ?? Flags[0];
    }

    [RelayCommand]
    private void ShowInstructors() => ActiveSubPanel = TileSubPanel.Instructors;

    [RelayCommand]
    private void ShowRoom() => ActiveSubPanel = TileSubPanel.Room;

    [RelayCommand]
    private void ShowTags() => ActiveSubPanel = TileSubPanel.Tags;

    [RelayCommand]
    private void ShowFlag() => ActiveSubPanel = TileSubPanel.Flag;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (_section is null) return;

        switch (ActiveSubPanel)
        {
            case TileSubPanel.Instructors:
                // The list is rebuilt, so each assignment that already existed must keep both its
                // workload and its unknown JSON fields (PersistedRecord.ExtensionData); only a
                // newly ticked instructor starts fresh (workload 1, no unknown fields).
                var existingAssignments = _section.InstructorAssignments
                    .ToDictionary(a => a.InstructorId);
                _section.InstructorAssignments = Instructors
                    .Where(i => i.IsChecked)
                    .Select(i =>
                    {
                        existingAssignments.TryGetValue(i.Id, out var original);
                        return new InstructorAssignment
                        {
                            InstructorId = i.Id,
                            Workload     = original?.Workload ?? 1m
                        }.WithExtensionDataFrom(original);
                    })
                    .ToList();
                break;

            case TileSubPanel.Room:
                var meeting = _section.Schedule
                    .FirstOrDefault(s => s.Day == _meetingDay && s.StartMinutes == _meetingStart);
                if (meeting is not null)
                    meeting.RoomId = string.IsNullOrEmpty(SelectedRoom?.Id) ? null : SelectedRoom.Id;
                break;

            case TileSubPanel.Tags:
                _section.TagIds = Tags.Where(t => t.IsChecked).Select(t => t.Id).ToList();
                break;

            case TileSubPanel.Flag:
                _section.Flag = SelectedFlag?.Value ?? SectionFlag.None;
                break;

            default:
                return;
        }

        _sectionRepo.Update(_section);
        _onSaved();
        IsOpen = false;
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;
}
