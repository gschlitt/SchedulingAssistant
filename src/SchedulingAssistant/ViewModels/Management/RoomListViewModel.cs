using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class RoomListViewModel : ViewModelBase
{
    private readonly IRoomRepository _repo;
    private readonly ISectionRepository _sectionRepo;
    private readonly SectionListViewModel _sectionListVm;
    private readonly IDatabaseContext _db;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    public string DisplayName => "Rooms";

    [ObservableProperty] private ObservableCollection<Room> _rooms = new();
    [ObservableProperty] private Room? _selectedRoom;
    [ObservableProperty] private RoomEditViewModel? _editVm;

    public RoomListViewModel(
        IRoomRepository repo,
        ISectionRepository sectionRepo,
        SectionListViewModel sectionListVm,
        IDatabaseContext db,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _repo = repo;
        _sectionRepo = sectionRepo;
        _sectionListVm = sectionListVm;
        _db = db;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load() =>
        Rooms = new ObservableCollection<Room>(_repo.GetAll());

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Re-evaluates the Move Up/Down button states whenever the selection changes.
    /// </summary>
    partial void OnSelectedRoomChanged(Room? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    private bool CanMoveUp()   => _lockService.IsWriter && SelectedRoom != null && Rooms.IndexOf(SelectedRoom) > 0;
    private bool CanMoveDown() => _lockService.IsWriter && SelectedRoom != null && Rooms.IndexOf(SelectedRoom) < Rooms.Count - 1;

    /// <summary>
    /// Moves the selected room one position earlier in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => ApplyMove(Rooms.IndexOf(SelectedRoom!), -1);

    /// <summary>
    /// Moves the selected room one position later in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => ApplyMove(Rooms.IndexOf(SelectedRoom!), +1);

    /// <summary>
    /// Reorders the room at <paramref name="index"/> by <paramref name="delta"/> positions
    /// (+1 or -1), then re-packs all sort orders as 0, 1, 2, … and saves every changed room.
    /// Reloads the list from DB so the UI reflects the stored order.
    /// </summary>
    /// <param name="index">Zero-based index of the room to move.</param>
    /// <param name="delta">Direction: -1 to move up, +1 to move down.</param>
    private void ApplyMove(int index, int delta)
    {
        var list = Rooms.ToList();
        var room = list[index];
        list.RemoveAt(index);
        list.Insert(index + delta, room);

        // Re-pack sort orders densely (0, 1, 2, …) to avoid gaps accumulating over time.
        for (var i = 0; i < list.Count; i++)
            list[i].SortOrder = i;

        foreach (var r in list)
            _repo.Update(r);

        var selectedId = room.Id;
        Load();
        SelectedRoom = Rooms.FirstOrDefault(r => r.Id == selectedId);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        // New rooms land at the end of the list.
        var room = new Room
        {
            SortOrder = Rooms.Count > 0 ? Rooms.Max(r => r.SortOrder) + 1 : 0
        };
        EditVm = new RoomEditViewModel(room, isNew: true,
            onSave: r => { _repo.Insert(r); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedRoom is null) return;
        var s = SelectedRoom;
        var copy = new Room
        {
            Id = s.Id, Building = s.Building, RoomNumber = s.RoomNumber,
            Capacity = s.Capacity, Features = s.Features, Notes = s.Notes,
            SortOrder = s.SortOrder,
        };
        EditVm = new RoomEditViewModel(copy, isNew: false,
            onSave: r => { _repo.Update(r); Load(); EditVm = null; _sectionListVm.Reload(); },
            onCancel: () => EditVm = null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedRoom is null) return;
        var id = SelectedRoom.Id;
        var displayName = $"{SelectedRoom.Building} {SelectedRoom.RoomNumber}".Trim();

        if (!await _dialog.Confirm(
            $"Delete room \"{displayName}\"?\n\nThis will also remove it from all sections in all semesters that reference it."))
            return;

        using var tx = _db.Connection.BeginTransaction();
        try
        {
            var sections = _sectionRepo.GetAll();
            foreach (var section in sections)
            {
                var changed = false;
                foreach (var sched in section.Schedule.Where(s => s.RoomId == id))
                {
                    sched.RoomId = null;
                    changed = true;
                }
                if (changed) _sectionRepo.Update(section, tx);
            }
            _repo.Delete(id, tx);
            tx.Commit();
            Load();
            _sectionListVm.Reload();
        }
        catch (Exception)
        {
            tx.Rollback();
            await _dialog.ShowError("The delete could not be completed. No changes were made. Please try again.");
        }
    }
}
