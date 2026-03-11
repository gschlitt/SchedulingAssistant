using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.GridView;

public enum TileSubPanel { None, Instructors, Room, Tags }

public partial class SectionContextMenuViewModel : ObservableObject
{
    private readonly SectionRepository _sectionRepo;
    private readonly Action _onSaved;

    private Section? _section;
    private int _meetingDay;
    private int _meetingStart;

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubPanel))]
    [NotifyPropertyChangedFor(nameof(IsInstructorPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsRoomPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsTagPanelVisible))]
    private TileSubPanel _activeSubPanel;

    public bool HasSubPanel          => ActiveSubPanel != TileSubPanel.None;
    public bool IsInstructorPanelVisible => ActiveSubPanel == TileSubPanel.Instructors;
    public bool IsRoomPanelVisible   => ActiveSubPanel == TileSubPanel.Room;
    public bool IsTagPanelVisible    => ActiveSubPanel == TileSubPanel.Tags;

    public ObservableCollection<ContextMenuItemVm> Instructors { get; } = [];
    public ObservableCollection<ContextMenuItemVm> Rooms       { get; } = [];
    public ObservableCollection<ContextMenuItemVm> Tags        { get; } = [];

    [ObservableProperty] private ContextMenuItemVm? _selectedRoom;

    public SectionContextMenuViewModel(SectionRepository sectionRepo, Action onSaved)
    {
        _sectionRepo = sectionRepo;
        _onSaved = onSaved;
    }

    public void Load(
        Section section,
        int meetingDay,
        int meetingStart,
        IEnumerable<Instructor> instructors,
        IEnumerable<Room> rooms,
        IEnumerable<SectionPropertyValue> tags)
    {
        _section      = section;
        _meetingDay   = meetingDay;
        _meetingStart = meetingStart;
        ActiveSubPanel = TileSubPanel.None;

        // Instructors — multi-select, active only
        Instructors.Clear();
        var assignedIds = section.InstructorIds.ToHashSet();
        foreach (var inst in instructors.Where(i => i.IsActive))
            Instructors.Add(new ContextMenuItemVm(
                inst.Id,
                $"{inst.FirstName} {inst.LastName}",
                assignedIds.Contains(inst.Id)));

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
    }

    [RelayCommand]
    private void ShowInstructors() => ActiveSubPanel = TileSubPanel.Instructors;

    [RelayCommand]
    private void ShowRoom() => ActiveSubPanel = TileSubPanel.Room;

    [RelayCommand]
    private void ShowTags() => ActiveSubPanel = TileSubPanel.Tags;

    [RelayCommand]
    private void Confirm()
    {
        if (_section is null) return;

        switch (ActiveSubPanel)
        {
            case TileSubPanel.Instructors:
                var existingWorkloads = _section.InstructorAssignments
                    .ToDictionary(a => a.InstructorId, a => a.Workload);
                _section.InstructorAssignments = Instructors
                    .Where(i => i.IsChecked)
                    .Select(i => new InstructorAssignment
                    {
                        InstructorId = i.Id,
                        Workload     = existingWorkloads.GetValueOrDefault(i.Id) ?? 1m
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
