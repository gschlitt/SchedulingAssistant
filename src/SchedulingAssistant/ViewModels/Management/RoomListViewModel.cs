using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class RoomListViewModel : ViewModelBase
{
    private readonly RoomRepository _repo;

    [ObservableProperty] private ObservableCollection<Room> _rooms = new();
    [ObservableProperty] private Room? _selectedRoom;
    [ObservableProperty] private RoomEditViewModel? _editVm;

    public RoomListViewModel(RoomRepository repo)
    {
        _repo = repo;
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
            onSave: r => { _repo.Update(r); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedRoom is null) return;
        _repo.Delete(SelectedRoom.Id);
        Load();
    }
}
