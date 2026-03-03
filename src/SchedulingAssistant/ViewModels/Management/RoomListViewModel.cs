using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class RoomListViewModel : ViewModelBase
{
    private readonly RoomRepository _repo;
    private readonly SectionRepository _sectionRepo;
    private readonly SectionListViewModel _sectionListVm;
    private readonly DatabaseContext _db;

    public string DisplayName => "Rooms";

    [ObservableProperty] private ObservableCollection<Room> _rooms = new();
    [ObservableProperty] private Room? _selectedRoom;
    [ObservableProperty] private RoomEditViewModel? _editVm;

    public Func<string, Task<bool>>? ConfirmDelete { get; set; }
    public Func<string, Task>? ShowError { get; set; }

    public RoomListViewModel(
        RoomRepository repo,
        SectionRepository sectionRepo,
        SectionListViewModel sectionListVm,
        DatabaseContext db)
    {
        _repo = repo;
        _sectionRepo = sectionRepo;
        _sectionListVm = sectionListVm;
        _db = db;
        Load();
    }

    private void Load() =>
        Rooms = new ObservableCollection<Room>(_repo.GetAll());

    [RelayCommand]
    private void Add()
    {
        var room = new Room();
        EditVm = new RoomEditViewModel(room, isNew: true,
            onSave: r => { _repo.Insert(r); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedRoom is null) return;
        var s = SelectedRoom;
        var copy = new Room { Id = s.Id, Building = s.Building, RoomNumber = s.RoomNumber, Capacity = s.Capacity, Features = s.Features, Notes = s.Notes };
        EditVm = new RoomEditViewModel(copy, isNew: false,
            onSave: r => { _repo.Update(r); Load(); EditVm = null; _sectionListVm.Reload(); },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedRoom is null) return;
        var id = SelectedRoom.Id;
        var displayName = $"{SelectedRoom.Building} {SelectedRoom.RoomNumber}".Trim();

        if (ConfirmDelete is not null)
        {
            var confirmed = await ConfirmDelete(displayName);
            if (!confirmed) return;
        }

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
            if (ShowError is not null)
                await ShowError("The delete could not be completed. No changes were made. Please try again.");
        }
    }
}
