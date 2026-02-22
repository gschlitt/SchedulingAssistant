using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class RoomEditViewModel : ViewModelBase
{
    [ObservableProperty] private string _building = string.Empty;
    [ObservableProperty] private string _roomNumber = string.Empty;
    [ObservableProperty] private int _capacity;
    [ObservableProperty] private string _features = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    public string Title => IsNew ? "Add Room" : "Edit Room";
    public bool IsNew { get; }

    private readonly Room _room;
    private readonly Action<Room> _onSave;
    private readonly Action _onCancel;

    public RoomEditViewModel(Room room, bool isNew, Action<Room> onSave, Action onCancel)
    {
        _room = room;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;

        Building = room.Building;
        RoomNumber = room.RoomNumber;
        Capacity = room.Capacity;
        Features = room.Features;
        Notes = room.Notes;
    }

    [RelayCommand]
    private void Save()
    {
        _room.Building = Building.Trim();
        _room.RoomNumber = RoomNumber.Trim();
        _room.Capacity = Capacity;
        _room.Features = Features.Trim();
        _room.Notes = Notes.Trim();
        _onSave(_room);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
