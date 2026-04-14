using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Rooms management panel. Provides a list of rooms with
/// inline Add/Edit/Delete and manual ordering support.
/// </summary>
public partial class RoomListViewModel : ViewModelBase, IDismissableEditor
{
    private readonly IRoomRepository _repo;
    private readonly ICampusRepository _campusRepo;
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

    [ObservableProperty] private ObservableCollection<RoomRow> _rooms = new();
    [ObservableProperty] private RoomRow? _selectedRow;
    [ObservableProperty] private RoomEditViewModel? _editVm;

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        if (EditVm is null) return false;
        EditVm.CancelCommand.Execute(null);
        return true;
    }

    /// <summary>Convenience accessor for the selected room model; null when nothing is selected.</summary>
    private Room? SelectedRoom => SelectedRow?.Room;

    /// <param name="repo">Room data repository.</param>
    /// <param name="campusRepo">Campus repository — supplies campus options to the edit form.</param>
    /// <param name="sectionRepo">Used during delete to clear the room from any sections that reference it.</param>
    /// <param name="sectionListVm">Reloaded after edits so the section list reflects name changes.</param>
    /// <param name="db">Database context for transaction support on delete.</param>
    /// <param name="dialog">Confirmation and error dialogs.</param>
    /// <param name="lockService">Write lock; gates edit operations in read-only mode.</param>
    public RoomListViewModel(
        IRoomRepository repo,
        ICampusRepository campusRepo,
        ISectionRepository sectionRepo,
        SectionListViewModel sectionListVm,
        IDatabaseContext db,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _repo = repo;
        _campusRepo = campusRepo;
        _sectionRepo = sectionRepo;
        _sectionListVm = sectionListVm;
        _db = db;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load()
    {
        var campusById = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
        Rooms = new ObservableCollection<RoomRow>(
            _repo.GetAll().Select(r => new RoomRow(r, ResolveCampus(r.CampusId, campusById))));
    }

    private static string ResolveCampus(string? campusId, Dictionary<string, string> lookup) =>
        campusId is not null && lookup.TryGetValue(campusId, out var name) ? name : string.Empty;

    /// <summary>Builds the campus option list for the edit form, with a leading "(none)" sentinel.</summary>
    private List<CampusOption> BuildCampusOptions() =>
        new List<CampusOption> { new(null, "(none)") }
            .Concat(_campusRepo.GetAll().Select(c => new CampusOption(c.Id, c.Name)))
            .ToList();

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
    partial void OnSelectedRowChanged(RoomRow? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    private bool CanMoveUp()   => _lockService.IsWriter && SelectedRow != null && Rooms.IndexOf(SelectedRow) > 0;
    private bool CanMoveDown() => _lockService.IsWriter && SelectedRow != null && Rooms.IndexOf(SelectedRow) < Rooms.Count - 1;

    /// <summary>
    /// Moves the selected room one position earlier in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => ApplyMove(Rooms.IndexOf(SelectedRow!), -1);

    /// <summary>
    /// Moves the selected room one position later in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => ApplyMove(Rooms.IndexOf(SelectedRow!), +1);

    /// <summary>
    /// Reorders the room row at <paramref name="index"/> by <paramref name="delta"/> positions
    /// (+1 or -1), then re-packs all sort orders as 0, 1, 2, … and saves every changed room.
    /// </summary>
    /// <param name="index">Zero-based index of the row to move.</param>
    /// <param name="delta">Direction: -1 to move up, +1 to move down.</param>
    private void ApplyMove(int index, int delta)
    {
        var list = Rooms.ToList();
        var row  = list[index];
        list.RemoveAt(index);
        list.Insert(index + delta, row);

        // Re-pack sort orders densely (0, 1, 2, …) to avoid gaps accumulating over time.
        for (var i = 0; i < list.Count; i++)
            list[i].Room.SortOrder = i;

        foreach (var r in list)
            _repo.Update(r.Room);

        var selectedId = row.Room.Id;
        Load();
        SelectedRow = Rooms.FirstOrDefault(r => r.Room.Id == selectedId);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    /// <summary>Opens the Add form for a new room at the end of the list.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var room = new Room
        {
            SortOrder = Rooms.Count > 0 ? Rooms.Max(r => r.Room.SortOrder) + 1 : 0
        };
        EditVm = new RoomEditViewModel(room, isNew: true,
            campusOptions: BuildCampusOptions(),
            onSave: r => { _repo.Insert(r); Load(); SelectedRow = Rooms.FirstOrDefault(x => x.Room.Id == r.Id); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    /// <summary>Opens the Edit form for the currently selected room.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedRoom is null) return;
        var s = SelectedRoom;
        var copy = new Room
        {
            Id         = s.Id,
            Building   = s.Building,
            RoomNumber = s.RoomNumber,
            Capacity   = s.Capacity,
            Features   = s.Features,
            Notes      = s.Notes,
            CampusId   = s.CampusId,
            SortOrder  = s.SortOrder,
        };
        EditVm = new RoomEditViewModel(copy, isNew: false,
            campusOptions: BuildCampusOptions(),
            onSave: r => { _repo.Update(r); Load(); SelectedRow = Rooms.FirstOrDefault(x => x.Room.Id == r.Id); EditVm = null; _sectionListVm.Reload(); },
            onCancel: () => EditVm = null);
    }

    /// <summary>
    /// Prompts for confirmation, then deletes the selected room and removes it from
    /// any sections that reference it (within a transaction).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedRoom is null) return;
        var id          = SelectedRoom.Id;
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

/// <summary>
/// Display-ready row for the Rooms data grid.
/// Pairs a <see cref="Room"/> model with its resolved campus display name.
/// </summary>
/// <param name="Room">The underlying room model.</param>
/// <param name="CampusName">Campus display name, or empty string when no campus is associated.</param>
public record RoomRow(Room Room, string CampusName);
