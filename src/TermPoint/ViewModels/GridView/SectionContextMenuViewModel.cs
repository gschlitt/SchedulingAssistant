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
    private readonly ISectionCardState _cardState;

    /// <summary>CanExecute predicate for the Confirm command; refuses writes in reader mode.</summary>
    private bool CanConfirm() => _lockService.IsWriter;

    private Section? _section;
    private int _meetingDay;
    private int _meetingStart;
    private int _meetingIndex = -1;

    [ObservableProperty] private bool _isOpen;

    /// <summary>
    /// Why the menu cannot edit right now: a section card is open in the Section List, and saving would reload the
    /// list and discard it. Null when editing is allowed. Set on <see cref="Load"/> and re-checked on Confirm; while
    /// non-null the four "Show" buttons are disabled. The wording is <see cref="OpenCardMessages.FinishFirst"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowInstructorsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowRoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowTagsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowFlagCommand))]
    private string? _refusalMessage;

    /// <summary>
    /// CanExecute predicate for the Show* commands: false while a section card is open, and false in read-only mode.
    /// Readers do get the menu (so they can mark sections, which saves nothing), but its edit items stay disabled.
    /// </summary>
    private bool CanShowSubPanel() => RefusalMessage is null && _lockService.IsWriter;

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

    private readonly SectionMarks _marks;

    /// <summary>
    /// Text of the mark button: "Mark Section" when the right-clicked section is not marked, "Unmark Section" when it
    /// is. Set on <see cref="Load"/>. Approved wording (Greg, 2026-11-16).
    /// </summary>
    [ObservableProperty] private string _markItemText = MarkText;

    /// <summary>
    /// True while any section is marked; the "Clear All Marks" button is shown only then. Set on <see cref="Load"/>
    /// and after each mark command. Approved wording of the button (Greg, 2026-11-16): "Clear All Marks".
    /// </summary>
    [ObservableProperty] private bool _hasAnyMarks;

    private const string MarkText   = "Mark Section";
    private const string UnmarkText = "Unmark Section";

    /// <summary>Creates the menu view model.</summary>
    /// <param name="sectionRepo">Repository the menu's edits are saved through.</param>
    /// <param name="onSaved">Called after a successful save.</param>
    /// <param name="lockService">The write lock; reader mode refuses edits (<see cref="CanConfirm"/>).</param>
    /// <param name="cardState">Whether a section card is open (refuses edits, see <see cref="RefusalMessage"/>).</param>
    /// <param name="marks">
    /// The grid's marked sections. Marking is deliberately not gated by <paramref name="lockService"/> or
    /// <paramref name="cardState"/>: it changes nothing saved.
    /// </param>
    public SectionContextMenuViewModel(ISectionRepository sectionRepo, Action onSaved, WriteLockService lockService,
        ISectionCardState cardState, SectionMarks marks)
    {
        _marks = marks;
        _cardState = cardState;
        _sectionRepo = sectionRepo;
        _onSaved = onSaved;
        _lockService = lockService;
    }

    /// <summary>
    /// Finds the meeting a tile stands for. The tile's index into <c>section.Schedule</c> is used when it is in
    /// range AND that meeting's day and start still equal the clicked ones; otherwise falls back to the first
    /// meeting at that day and start.
    /// <para>
    /// Why both: two meetings of one section can share a day and start (odd/even weeks), so (day, start) alone
    /// picks the wrong one; but a tile can outlive a reload that reordered or changed the schedule, so an index
    /// that no longer matches its day and start must not be trusted, or the edit would land on the wrong meeting.
    /// </para>
    /// </summary>
    /// <param name="section">The section to search.</param>
    /// <param name="meetingIndex">The tile's meeting index, or -1.</param>
    /// <param name="day">The clicked day.</param>
    /// <param name="start">The clicked start, in minutes from midnight.</param>
    /// <returns>The meeting, or null when none matches.</returns>
    private static SectionDaySchedule? FindMeeting(Section section, int meetingIndex, int day, int start)
    {
        if (meetingIndex >= 0 && meetingIndex < section.Schedule.Count)
        {
            var indexed = section.Schedule[meetingIndex];
            if (indexed.Day == day && indexed.StartMinutes == start) return indexed;
        }
        return section.Schedule.FirstOrDefault(s => s.Day == day && s.StartMinutes == start);
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
    /// <param name="meetingIndex">
    /// Index of the clicked meeting in <c>section.Schedule</c>, carried on the tile; -1 when unknown. See
    /// <see cref="FindMeeting"/> for how it is combined with the day and start.
    /// </param>
    public void Load(
        Section section,
        int meetingDay,
        int meetingStart,
        IEnumerable<Instructor> instructors,
        IEnumerable<Room> rooms,
        IEnumerable<SchedulingEnvironmentValue> tags,
        IReadOnlySet<string>? outOfFocusInstructorIds = null,
        int meetingIndex = -1)
    {
        _section      = section;
        _meetingIndex = meetingIndex;
        _meetingDay   = meetingDay;
        _meetingStart = meetingStart;
        ActiveSubPanel = TileSubPanel.None;
        RefusalMessage = OpenCardMessages.WhyRefused(_cardState);
        // The write lock may have changed since the last Load, and RefusalMessage only re-raises when its value changes.
        ShowInstructorsCommand.NotifyCanExecuteChanged();
        ShowRoomCommand.NotifyCanExecuteChanged();
        ShowTagsCommand.NotifyCanExecuteChanged();
        ShowFlagCommand.NotifyCanExecuteChanged();
        RefreshMarkState();

        // Instructors — multi-select: active instructors, plus any inactive instructor the section already has.
        // Confirm rebuilds the assignments from the ticked rows, so an assigned instructor missing from the list
        // would be silently unassigned; listing them, ticked, keeps them.
        // Under chosen subjects the caller has ordered the list in-focus first; out-of-focus rows are
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
        var meeting = FindMeeting(section, meetingIndex, meetingDay, meetingStart);
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

    [RelayCommand(CanExecute = nameof(CanShowSubPanel))]
    private void ShowInstructors() => ActiveSubPanel = TileSubPanel.Instructors;

    [RelayCommand(CanExecute = nameof(CanShowSubPanel))]
    private void ShowRoom() => ActiveSubPanel = TileSubPanel.Room;

    [RelayCommand(CanExecute = nameof(CanShowSubPanel))]
    private void ShowTags() => ActiveSubPanel = TileSubPanel.Tags;

    [RelayCommand(CanExecute = nameof(CanShowSubPanel))]
    private void ShowFlag() => ActiveSubPanel = TileSubPanel.Flag;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (_section is null) return;

        // A card may have been opened since Load; saving now would reload the list and discard it.
        var refusal = OpenCardMessages.WhyRefused(_cardState);
        if (refusal is not null)
        {
            RefusalMessage = refusal;
            ActiveSubPanel = TileSubPanel.None;
            return;
        }

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
                var meeting = FindMeeting(_section, _meetingIndex, _meetingDay, _meetingStart);
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

    /// <summary>Refreshes <see cref="MarkItemText"/> and <see cref="HasAnyMarks"/> from the marks and the loaded section.</summary>
    private void RefreshMarkState()
    {
        MarkItemText = _section is not null && _marks.IsMarked(_section.Id) ? UnmarkText : MarkText;
        HasAnyMarks  = _marks.HasAny;
    }

    /// <summary>
    /// Marks the loaded section, or unmarks it if it is marked, then closes the menu. Never gated by the write lock or
    /// an open card (marks are not saved). Does nothing when no section is loaded.
    /// </summary>
    [RelayCommand]
    private void ToggleMark()
    {
        if (_section is null) return;
        _marks.Toggle(_section.Id);
        RefreshMarkState();
        IsOpen = false;
    }

    /// <summary>Unmarks every section, then closes the menu. Does nothing when no section is loaded.</summary>
    [RelayCommand]
    private void ClearMarks()
    {
        if (_section is null) return;
        _marks.Clear();
        RefreshMarkState();
        IsOpen = false;
    }
}
